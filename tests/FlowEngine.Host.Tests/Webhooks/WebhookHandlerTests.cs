using System.Net;
using System.Security.Cryptography;
using System.Text;
using FlowEngine.Application.Audit;
using FlowEngine.Application.Identity;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.ValueObjects;
using FlowEngine.Host.Options;
using FlowEngine.Host.Webhooks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace FlowEngine.Host.Tests.Webhooks;

/// <summary>
/// WebhookHandler 单元测试，覆盖路由查询、签名校验、幂等，以及安全加固（SEC-3 重放/限流、
/// EX-4 事件驱动同步）。签名使用不可 seek 的请求体流以回归 B8（非 Kestrel 流上 Position 回退崩溃）。
/// </summary>
public class WebhookHandlerTests
{
    private readonly Mock<IEngine> _engine = new();
    private readonly Mock<IEventBus> _eventBus = new();
    private readonly Mock<IExecutionIdempotencyService> _idempotency = new();
    private readonly Mock<ILogger<WebhookHandler>> _logger = new();
    private readonly Mock<IUserContext> _userContext = new();

    private AuditEventFactory AuditFactory => new(_userContext.Object);

    private WebhookHandler CreateHandler(
        FlowEngineDbContext db,
        WebhookReplayCache? replayCache = null,
        WebhookRateLimiter? rateLimiter = null,
        IWebhookSyncCompletionService? sync = null,
        WebhookSecurityOptions? securityOptions = null)
    {
        securityOptions ??= new WebhookSecurityOptions();
        var secOpts = Microsoft.Extensions.Options.Options.Create(securityOptions);
        replayCache ??= new WebhookReplayCache(secOpts);
        rateLimiter ??= new WebhookRateLimiter(secOpts);
        sync ??= new FakeSyncCompletion(ExecutionStatus.Completed);
        return new WebhookHandler(
            db,
            _engine.Object,
            _eventBus.Object,
            AuditFactory,
            _idempotency.Object,
            _logger.Object,
            Microsoft.Extensions.Options.Options.Create(new WebhookOptions()),
            replayCache,
            rateLimiter,
            secOpts,
            sync);
    }

    private static string SignBody(string secret, string timestamp, string nonce, string body)
        => $"sha256={Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{nonce}.{body}"))).ToLowerInvariant()}";

    private static HttpContext CreateContext(
        string? body = null,
        string? signature = null,
        string? remoteIp = null,
        string? timestamp = null,
        string? nonce = null)
    {
        var context = new DefaultHttpContext
        {
            Response = { Body = new MemoryStream() },
        };

        if (remoteIp is not null)
        {
            context.Connection.RemoteIpAddress = IPAddress.Parse(remoteIp);
        }

        if (body is not null)
        {
            // 不可 seek 的流，模拟 Kestrel 请求体（B8 回归关键）。
            context.Request.Body = new NonSeekableStream(Encoding.UTF8.GetBytes(body));
            context.Request.ContentLength = Encoding.UTF8.GetByteCount(body);
        }

        if (signature is not null)
        {
            context.Request.Headers["X-Hub-Signature-256"] = signature;
        }

        if (timestamp is not null)
        {
            context.Request.Headers["X-Webhook-Timestamp"] = timestamp;
        }

        if (nonce is not null)
        {
            context.Request.Headers["X-Webhook-Nonce"] = nonce;
        }

        return context;
    }

    private static FlowEngineDbContext CreateDb(string name)
    {
        var options = new DbContextOptionsBuilder<FlowEngineDbContext>()
            .UseInMemoryDatabase(databaseName: name)
            .Options;
        return new FlowEngineDbContext(options);
    }

    private static async Task<string> ReadResponseBody(HttpContext context)
    {
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }

    private static string NowTs() => DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

    [Fact]
    public async Task RouteNotFound_Returns404_AndDoesNotStartWorkflow()
    {
        var db = CreateDb(nameof(RouteNotFound_Returns404_AndDoesNotStartWorkflow));
        var handler = CreateHandler(db);
        var context = CreateContext();

        await handler.HandleAsync(context, "/hooks/missing");

        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        _engine.Verify(e => e.StartAsync(It.IsAny<Guid>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TriggerInactive_Returns404_AndDoesNotStartWorkflow()
    {
        var db = CreateDb(nameof(TriggerInactive_Returns404_AndDoesNotStartWorkflow));
        var routeId = Guid.NewGuid();
        var triggerId = Guid.NewGuid();
        db.WebhookRoutes.Add(new WebhookRoute
        {
            Id = routeId,
            Path = "/hooks/x",
            WorkflowDefinitionId = Guid.NewGuid(),
            TriggerId = triggerId,
            Secret = null,
            AllowedIps = null,
        });
        db.Triggers.Add(new Trigger
        {
            Id = triggerId,
            Type = TriggerType.Webhook,
            IsActive = false,
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = CreateHandler(db);
        var context = CreateContext();

        await handler.HandleAsync(context, "/hooks/x");

        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        _engine.Verify(e => e.StartAsync(It.IsAny<Guid>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task NoSecretAndNoAllowlist_Returns401()
    {
        var db = CreateDb(nameof(NoSecretAndNoAllowlist_Returns401));
        var routeId = Guid.NewGuid();
        var triggerId = Guid.NewGuid();
        db.WebhookRoutes.Add(new WebhookRoute
        {
            Id = routeId,
            Path = "/hooks/x",
            WorkflowDefinitionId = Guid.NewGuid(),
            TriggerId = triggerId,
            Secret = null,
            AllowedIps = null,
        });
        db.Triggers.Add(new Trigger { Id = triggerId, Type = TriggerType.Webhook, IsActive = true });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = CreateHandler(db);
        var context = CreateContext(body: "{}");

        await handler.HandleAsync(context, "/hooks/x");

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        _engine.Verify(e => e.StartAsync(It.IsAny<Guid>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MissingTimestampOrNonce_Returns400()
    {
        // SEC-3：重放保护要求携带 X-Webhook-Timestamp 与 X-Webhook-Nonce。
        var db = CreateDb(nameof(MissingTimestampOrNonce_Returns400));
        var triggerId = Guid.NewGuid();
        db.WebhookRoutes.Add(new WebhookRoute
        {
            Id = Guid.NewGuid(),
            Path = "/hooks/x",
            WorkflowDefinitionId = Guid.NewGuid(),
            TriggerId = triggerId,
            Secret = "s3cr3t",
            AllowedIps = null,
        });
        db.Triggers.Add(new Trigger { Id = triggerId, Type = TriggerType.Webhook, IsActive = true });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = CreateHandler(db);
        var context = CreateContext(body: "{\"a\":1}"); // 无 timestamp/nonce

        await handler.HandleAsync(context, "/hooks/x");

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        _engine.Verify(e => e.StartAsync(It.IsAny<Guid>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReplayProtectionDisabled_HeaderlessRequest_StartsWorkflow()
    {
        // SEC-3 / I-2：禁用重放保护且无签名密钥时，缺失 X-Webhook-Timestamp/X-Webhook-Nonce
        // 不应被 400 拒绝（与 WebhookReplayCache/WebhookRateLimiter 的实际开关保持一致）。
        var db = CreateDb(nameof(ReplayProtectionDisabled_HeaderlessRequest_StartsWorkflow));
        var triggerId = Guid.NewGuid();
        db.WebhookRoutes.Add(new WebhookRoute
        {
            Id = Guid.NewGuid(),
            Path = "/hooks/x",
            WorkflowDefinitionId = Guid.NewGuid(),
            TriggerId = triggerId,
            Secret = null,
            AllowedIps = ["127.0.0.1"],
        });
        db.Triggers.Add(new Trigger { Id = triggerId, Type = TriggerType.Webhook, IsActive = true });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var executionId = Guid.NewGuid();
        _engine
            .Setup(e => e.StartAsync(It.IsAny<Guid>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExecutionId(executionId));

        var handler = CreateHandler(db, securityOptions: new WebhookSecurityOptions { EnableReplayProtection = false });
        var context = CreateContext(body: "{\"a\":1}", remoteIp: "127.0.0.1"); // 无 timestamp/nonce

        await handler.HandleAsync(context, "/hooks/x");

        Assert.Equal(StatusCodes.Status202Accepted, context.Response.StatusCode);
        _engine.Verify(e => e.StartAsync(It.IsAny<Guid>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MissingSignature_Returns401()
    {
        var db = CreateDb(nameof(MissingSignature_Returns401));
        var triggerId = Guid.NewGuid();
        db.WebhookRoutes.Add(new WebhookRoute
        {
            Id = Guid.NewGuid(),
            Path = "/hooks/x",
            WorkflowDefinitionId = Guid.NewGuid(),
            TriggerId = triggerId,
            Secret = "s3cr3t",
            AllowedIps = null,
        });
        db.Triggers.Add(new Trigger { Id = triggerId, Type = TriggerType.Webhook, IsActive = true });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = CreateHandler(db);
        var body = "{\"a\":1}";
        var context = CreateContext(body: body, timestamp: NowTs(), nonce: "n1"); // 无签名头

        await handler.HandleAsync(context, "/hooks/x");

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        _engine.Verify(e => e.StartAsync(It.IsAny<Guid>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task InvalidSignature_Returns401()
    {
        // B8 回归：使用不可 seek 的请求体流，验证签名校验路径不再依赖 Position 回退。
        var db = CreateDb(nameof(InvalidSignature_Returns401));
        var triggerId = Guid.NewGuid();
        const string secret = "s3cr3t";
        db.WebhookRoutes.Add(new WebhookRoute
        {
            Id = Guid.NewGuid(),
            Path = "/hooks/x",
            WorkflowDefinitionId = Guid.NewGuid(),
            TriggerId = triggerId,
            Secret = secret,
            AllowedIps = null,
        });
        db.Triggers.Add(new Trigger { Id = triggerId, Type = TriggerType.Webhook, IsActive = true });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = CreateHandler(db);
        var body = "{\"a\":1}";
        var ts = NowTs();
        var context = CreateContext(body: body, signature: SignBody(secret, ts, "n1", "tampered-body"), timestamp: ts, nonce: "n1");

        await handler.HandleAsync(context, "/hooks/x");

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        _engine.Verify(e => e.StartAsync(It.IsAny<Guid>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ValidSignature_StartsWorkflow_Returns202()
    {
        var db = CreateDb(nameof(ValidSignature_StartsWorkflow_Returns202));
        var triggerId = Guid.NewGuid();
        const string secret = "s3cr3t";
        db.WebhookRoutes.Add(new WebhookRoute
        {
            Id = Guid.NewGuid(),
            Path = "/hooks/x",
            WorkflowDefinitionId = Guid.NewGuid(),
            TriggerId = triggerId,
            Secret = secret,
            AllowedIps = null,
        });
        db.Triggers.Add(new Trigger { Id = triggerId, Type = TriggerType.Webhook, IsActive = true });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var executionId = Guid.NewGuid();
        _engine
            .Setup(e => e.StartAsync(It.IsAny<Guid>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExecutionId(executionId));

        var handler = CreateHandler(db);
        var body = "{\"a\":1}";
        var ts = NowTs();
        var context = CreateContext(body: body, signature: SignBody(secret, ts, "n1", body), timestamp: ts, nonce: "n1");

        await handler.HandleAsync(context, "/hooks/x");

        Assert.Equal(StatusCodes.Status202Accepted, context.Response.StatusCode);
        _engine.Verify(e => e.StartAsync(It.IsAny<Guid>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ValidSignature_EmptyBody_Returns202()
    {
        // 覆盖 rawBody == null 分支：空 body 时 ContentLength == 0，
        // 签名以 string.Empty 参与 HMAC（见 WebhookHandler 注释契约）。
        var db = CreateDb(nameof(ValidSignature_EmptyBody_Returns202));
        var triggerId = Guid.NewGuid();
        const string secret = "s3cr3t";
        db.WebhookRoutes.Add(new WebhookRoute
        {
            Id = Guid.NewGuid(),
            Path = "/hooks/x",
            WorkflowDefinitionId = Guid.NewGuid(),
            TriggerId = triggerId,
            Secret = secret,
            AllowedIps = null,
        });
        db.Triggers.Add(new Trigger { Id = triggerId, Type = TriggerType.Webhook, IsActive = true });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var executionId = Guid.NewGuid();
        _engine
            .Setup(e => e.StartAsync(It.IsAny<Guid>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExecutionId(executionId));

        var handler = CreateHandler(db);
        var ts = NowTs();
        var context = CreateContext(body: string.Empty, signature: SignBody(secret, ts, "n1", string.Empty), timestamp: ts, nonce: "n1");

        await handler.HandleAsync(context, "/hooks/x");

        Assert.Equal(StatusCodes.Status202Accepted, context.Response.StatusCode);
        _engine.Verify(e => e.StartAsync(It.IsAny<Guid>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task IdempotencyHit_Returns200Idempotent_AndDoesNotStartWorkflow()
    {
        var db = CreateDb(nameof(IdempotencyHit_Returns200Idempotent_AndDoesNotStartWorkflow));
        var triggerId = Guid.NewGuid();
        const string secret = "s3cr3t";
        db.WebhookRoutes.Add(new WebhookRoute
        {
            Id = Guid.NewGuid(),
            Path = "/hooks/x",
            WorkflowDefinitionId = Guid.NewGuid(),
            TriggerId = triggerId,
            Secret = secret,
            AllowedIps = null,
        });
        db.Triggers.Add(new Trigger
        {
            Id = triggerId,
            Type = TriggerType.Webhook,
            IsActive = true,
            Settings = new TriggerSettings
            {
                IdempotencyKeyTemplate = "{body.id}",
                IdempotencyTtlSeconds = 60,
            },
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var existingId = Guid.NewGuid();
        _idempotency
            .Setup(x => x.TryGetExistingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingId);

        var handler = CreateHandler(db);
        var body = "{\"id\":\"evt-9\"}";
        var ts = NowTs();
        var context = CreateContext(body: body, signature: SignBody(secret, ts, "n1", body), timestamp: ts, nonce: "n1");

        await handler.HandleAsync(context, "/hooks/x");

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        var response = await ReadResponseBody(context);
        Assert.Contains("Idempotent", response, StringComparison.OrdinalIgnoreCase);
        _engine.Verify(e => e.StartAsync(It.IsAny<Guid>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Replay_SameNonceRejected_Returns401()
    {
        // SEC-3：同一 (路由, nonce) 第二次提交被重放保护拒绝。
        var db = CreateDb(nameof(Replay_SameNonceRejected_Returns401));
        var triggerId = Guid.NewGuid();
        const string secret = "s3cr3t";
        db.WebhookRoutes.Add(new WebhookRoute
        {
            Id = Guid.NewGuid(),
            Path = "/hooks/x",
            WorkflowDefinitionId = Guid.NewGuid(),
            TriggerId = triggerId,
            Secret = secret,
            AllowedIps = null,
        });
        db.Triggers.Add(new Trigger { Id = triggerId, Type = TriggerType.Webhook, IsActive = true });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var executionId = Guid.NewGuid();
        _engine
            .Setup(e => e.StartAsync(It.IsAny<Guid>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExecutionId(executionId));

        var handler = CreateHandler(db, replayCache: new WebhookReplayCache(Microsoft.Extensions.Options.Options.Create(new WebhookSecurityOptions())));
        var body = "{\"a\":1}";
        var ts = NowTs();
        var nonce = "replay-nonce";

        // 首次提交：成功。
        var ctx1 = CreateContext(body: body, signature: SignBody(secret, ts, nonce, body), timestamp: ts, nonce: nonce, remoteIp: "127.0.0.1");
        await handler.HandleAsync(ctx1, "/hooks/x");
        Assert.Equal(StatusCodes.Status202Accepted, ctx1.Response.StatusCode);

        // 重放同一 nonce：拒绝。
        var ctx2 = CreateContext(body: body, signature: SignBody(secret, ts, nonce, body), timestamp: ts, nonce: nonce, remoteIp: "127.0.0.1");
        await handler.HandleAsync(ctx2, "/hooks/x");
        Assert.Equal(StatusCodes.Status401Unauthorized, ctx2.Response.StatusCode);
        _engine.Verify(e => e.StartAsync(It.IsAny<Guid>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExpiredTimestamp_Returns401()
    {
        // SEC-3：超出重放窗口的时间戳被拒绝。
        var db = CreateDb(nameof(ExpiredTimestamp_Returns401));
        var triggerId = Guid.NewGuid();
        const string secret = "s3cr3t";
        db.WebhookRoutes.Add(new WebhookRoute
        {
            Id = Guid.NewGuid(),
            Path = "/hooks/x",
            WorkflowDefinitionId = Guid.NewGuid(),
            TriggerId = triggerId,
            Secret = secret,
            AllowedIps = null,
        });
        db.Triggers.Add(new Trigger { Id = triggerId, Type = TriggerType.Webhook, IsActive = true });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = CreateHandler(db);
        var body = "{\"a\":1}";
        var expiredTs = (DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 600).ToString(); // 10 分钟前
        var context = CreateContext(body: body, signature: SignBody(secret, expiredTs, "n1", body), timestamp: expiredTs, nonce: "n1");

        await handler.HandleAsync(context, "/hooks/x");

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        _engine.Verify(e => e.StartAsync(It.IsAny<Guid>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RateLimit_ExceedingLimit_Returns429()
    {
        // SEC-3：每路由/IP 超过配额后返回 429。
        var db = CreateDb(nameof(RateLimit_ExceedingLimit_Returns429));
        var triggerId = Guid.NewGuid();
        const string secret = "s3cr3t";
        db.WebhookRoutes.Add(new WebhookRoute
        {
            Id = Guid.NewGuid(),
            Path = "/hooks/x",
            WorkflowDefinitionId = Guid.NewGuid(),
            TriggerId = triggerId,
            Secret = secret,
            AllowedIps = null,
        });
        db.Triggers.Add(new Trigger { Id = triggerId, Type = TriggerType.Webhook, IsActive = true });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var executionId = Guid.NewGuid();
        _engine
            .Setup(e => e.StartAsync(It.IsAny<Guid>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExecutionId(executionId));

        var rateOpts = Microsoft.Extensions.Options.Options.Create(new WebhookSecurityOptions
        {
            EnableRateLimit = true,
            RateLimitPermitCount = 2,
            RateLimitWindowSeconds = 60,
        });
        var handler = CreateHandler(db, rateLimiter: new WebhookRateLimiter(rateOpts));
        var ts = NowTs();

        for (var i = 0; i < 3; i++)
        {
            var nonce = $"n{i}";
            var body = $"{{\"i\":{i}}}";
            var ctx = CreateContext(body: body, signature: SignBody(secret, ts, nonce, body), timestamp: ts, nonce: nonce, remoteIp: "127.0.0.1");
            await handler.HandleAsync(ctx, "/hooks/x");
            if (i < 2)
            {
                Assert.Equal(StatusCodes.Status202Accepted, ctx.Response.StatusCode);
            }
            else
            {
                Assert.Equal(StatusCodes.Status429TooManyRequests, ctx.Response.StatusCode);
            }
        }

        _engine.Verify(e => e.StartAsync(It.IsAny<Guid>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task SyncRoute_EventDriven_Completes_Returns200()
    {
        // EX-4：同步 Webhook 改为事件驱动等待，不轮询 ExecutionRecords。
        var db = CreateDb(nameof(SyncRoute_EventDriven_Completes_Returns200));
        var triggerId = Guid.NewGuid();
        var workflowId = Guid.NewGuid();
        db.WebhookRoutes.Add(new WebhookRoute
        {
            Id = Guid.NewGuid(),
            Path = "/hooks/x",
            WorkflowDefinitionId = workflowId,
            TriggerId = triggerId,
            Secret = null,
            AllowedIps = ["127.0.0.1"],
            IsSync = true,
            MaxWaitSeconds = 5,
        });
        db.Triggers.Add(new Trigger { Id = triggerId, Type = TriggerType.Webhook, IsActive = true });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var executionId = Guid.NewGuid();
        _engine
            .Setup(e => e.StartAsync(It.IsAny<Guid>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExecutionId(executionId));

        var handler = CreateHandler(db, sync: new FakeSyncCompletion(ExecutionStatus.Completed));
        var context = CreateContext(remoteIp: "127.0.0.1", timestamp: NowTs(), nonce: "n1");

        await handler.HandleAsync(context, "/hooks/x");

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        var response = await ReadResponseBody(context);
        Assert.Contains("Completed", response, StringComparison.OrdinalIgnoreCase);
        _engine.Verify(e => e.StartAsync(It.IsAny<Guid>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SyncRoute_EventDriven_Timeout_Returns202()
    {
        // EX-4：等待完成事件超时（未收到）时返回 202 Timeout，而非轮询僵死。
        var db = CreateDb(nameof(SyncRoute_EventDriven_Timeout_Returns202));
        var triggerId = Guid.NewGuid();
        db.WebhookRoutes.Add(new WebhookRoute
        {
            Id = Guid.NewGuid(),
            Path = "/hooks/x",
            WorkflowDefinitionId = Guid.NewGuid(),
            TriggerId = triggerId,
            Secret = null,
            AllowedIps = ["127.0.0.1"],
            IsSync = true,
            MaxWaitSeconds = 1,
        });
        db.Triggers.Add(new Trigger { Id = triggerId, Type = TriggerType.Webhook, IsActive = true });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var executionId = Guid.NewGuid();
        _engine
            .Setup(e => e.StartAsync(It.IsAny<Guid>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExecutionId(executionId));

        var handler = CreateHandler(db, sync: new FakeSyncCompletion(timeout: true));
        var context = CreateContext(remoteIp: "127.0.0.1", timestamp: NowTs(), nonce: "n1");

        await handler.HandleAsync(context, "/hooks/x");

        Assert.Equal(StatusCodes.Status202Accepted, context.Response.StatusCode);
        var response = await ReadResponseBody(context);
        Assert.Contains("Timeout", response, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AsyncRoute_Returns202()
    {
        var db = CreateDb(nameof(AsyncRoute_Returns202));
        var triggerId = Guid.NewGuid();
        db.WebhookRoutes.Add(new WebhookRoute
        {
            Id = Guid.NewGuid(),
            Path = "/hooks/x",
            WorkflowDefinitionId = Guid.NewGuid(),
            TriggerId = triggerId,
            Secret = null,
            AllowedIps = ["127.0.0.1"],
            IsSync = false,
        });
        db.Triggers.Add(new Trigger { Id = triggerId, Type = TriggerType.Webhook, IsActive = true });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var executionId = Guid.NewGuid();
        _engine
            .Setup(e => e.StartAsync(It.IsAny<Guid>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExecutionId(executionId));

        var handler = CreateHandler(db);
        var context = CreateContext(remoteIp: "127.0.0.1", timestamp: NowTs(), nonce: "n1");

        await handler.HandleAsync(context, "/hooks/x");

        Assert.Equal(StatusCodes.Status202Accepted, context.Response.StatusCode);
        _engine.Verify(e => e.StartAsync(It.IsAny<Guid>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// 测试用同步完成服务：直接返回预设状态或模拟超时，避免依赖真实事件总线。
    /// </summary>
    private sealed class FakeSyncCompletion : IWebhookSyncCompletionService
    {
        private readonly ExecutionStatus _result;
        private readonly bool _timeout;

        public FakeSyncCompletion(ExecutionStatus result = ExecutionStatus.Completed, bool timeout = false)
        {
            _result = result;
            _timeout = timeout;
        }

        public Task<ExecutionStatus> WaitAsync(Guid executionId, TimeSpan timeout, CancellationToken ct)
        {
            if (_timeout)
            {
                return Task.FromException<ExecutionStatus>(new OperationCanceledException("timeout"));
            }

            return Task.FromResult(_result);
        }

        public void Complete(Guid executionId, ExecutionStatus status)
        {
        }
    }

    /// <summary>
    /// 不可 seek 的流，模拟 Kestrel 请求体，用于验证 B8 修复（不再依赖 Position 回退）。
    /// </summary>
    private sealed class NonSeekableStream : Stream
    {
        private readonly MemoryStream _inner;

        public NonSeekableStream(byte[] data) => _inner = new MemoryStream(data);

        public override bool CanSeek => false;
        public override bool CanRead => true;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => _inner.Flush();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => _inner.ReadAsync(buffer, offset, count, cancellationToken);
    }
}

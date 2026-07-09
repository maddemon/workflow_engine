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
using FlowEngine.Host.Webhooks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace FlowEngine.Host.Tests.Webhooks;

/// <summary>
/// WebhookHandler 单元测试，覆盖路由查询、签名校验、幂等、同步/异步分支。
/// 签名相关用例使用不可 seek 的请求体流，以回归 B8（非 Kestrel 流上 Position 回退崩溃）。
/// </summary>
public class WebhookHandlerTests
{
    private readonly Mock<IEngine> _engine = new();
    private readonly Mock<IEventBus> _eventBus = new();
    private readonly Mock<IExecutionIdempotencyService> _idempotency = new();
    private readonly Mock<ILogger<WebhookHandler>> _logger = new();
    private readonly Mock<IUserContext> _userContext = new();

    private AuditEventFactory AuditFactory => new(_userContext.Object);

    private WebhookHandler CreateHandler(FlowEngineDbContext db)
        => new(db, _engine.Object, _eventBus.Object, AuditFactory, _idempotency.Object, _logger.Object);

    private static string SignBody(string secret, string body)
    {
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body));
        return $"sha256={Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    private static HttpContext CreateContext(string? body = null, string? signature = null, string? remoteIp = null)
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
        var context = CreateContext(body: "{\"a\":1}");

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
        var context = CreateContext(body: body, signature: SignBody(secret, "tampered-body"));

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
        var context = CreateContext(body: body, signature: SignBody(secret, body));

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
        var context = CreateContext(body: string.Empty, signature: SignBody(secret, string.Empty));

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
        var context = CreateContext(body: body, signature: SignBody(secret, body));

        await handler.HandleAsync(context, "/hooks/x");

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        var response = await ReadResponseBody(context);
        Assert.Contains("Idempotent", response, StringComparison.OrdinalIgnoreCase);
        _engine.Verify(e => e.StartAsync(It.IsAny<Guid>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SyncRoute_ExecutionCompletes_Returns200()
    {
        var db = CreateDb(nameof(SyncRoute_ExecutionCompletes_Returns200));
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

        // 预置已完成执行记录，供同步轮询命中。
        db.ExecutionRecords.Add(new ExecutionRecord
        {
            Id = executionId,
            WorkflowDefinitionId = workflowId,
            Status = ExecutionStatus.Completed,
            StartedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = CreateHandler(db);
        var context = CreateContext(remoteIp: "127.0.0.1");

        await handler.HandleAsync(context, "/hooks/x");

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        _engine.Verify(e => e.StartAsync(It.IsAny<Guid>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
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
        var context = CreateContext(remoteIp: "127.0.0.1");

        await handler.HandleAsync(context, "/hooks/x");

        Assert.Equal(StatusCodes.Status202Accepted, context.Response.StatusCode);
        _engine.Verify(e => e.StartAsync(It.IsAny<Guid>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
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

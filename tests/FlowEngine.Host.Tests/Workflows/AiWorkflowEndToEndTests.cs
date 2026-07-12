using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FlowEngine.Application.Dtos;
using FlowEngine.Application.Identity;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Ai;
using FlowEngine.Core.Authorization;
using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Identity;
using FlowEngine.Host.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace FlowEngine.Host.Tests.Workflows;

/// <summary>
/// AI 工作流端到端集成测试：Catalog → assemble → confirm → execute。
/// 验证 AI-native API 全链路可成功创建并执行工作流。
/// </summary>
public class AiWorkflowEndToEndTests : IClassFixture<FlowEngineWebApplicationFactory>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _tempRoot;

    public AiWorkflowEndToEndTests(FlowEngineWebApplicationFactory factory)
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "flowengine-tests", Guid.NewGuid().ToString());
        var dbDirectory = Path.Combine(_tempRoot, "db");
        var auditDirectory = Path.Combine(_tempRoot, "audit");
        Directory.CreateDirectory(dbDirectory);
        Directory.CreateDirectory(auditDirectory);

        var dbPath = Path.Combine(dbDirectory, "flowengine.db");
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Default", $"Data Source={dbPath};Mode=ReadWriteCreate");
            builder.UseSetting("ExecutionCleanup:Enabled", "false");
            builder.UseSetting("Audit:LogPath", auditDirectory);
            builder.ConfigureServices(services =>
            {
                services.Replace(ServiceDescriptor.Singleton<IScheduleManager, NoOpScheduleManager>());
                services.RemoveAll<IHostedService>();
            });
        });

        _factory.ClientOptions.BaseAddress = new Uri("http://localhost");
    }

    public void Dispose()
    {
        _factory.Dispose();
        try
        {
            Directory.Delete(_tempRoot, true);
        }
        catch
        {
            // 忽略清理临时目录时的错误，不影响测试结果。
        }
    }

    [Fact]
    public async Task AiWorkflow_EndToEnd_CatalogAssembleConfirmExecute_Succeeds()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await CreateAuthenticatedClientAsync("ai-e2e@example.com", [RoleConstants.Admin], ct);

        // ── 1. Catalog：获取节点目录，AI 据此发现可用节点 ──
        var catalogResponse = await client.GetAsync("/api/v1/node-catalog", ct);
        Assert.Equal(HttpStatusCode.OK, catalogResponse.StatusCode);
        var catalog = await catalogResponse.Content.ReadFromJsonAsync<IReadOnlyList<AiNodeSummary>>(TestJsonOptions, ct);
        Assert.NotNull(catalog);
        Assert.Contains(catalog, n => n.Name == "manualTrigger");
        Assert.Contains(catalog, n => n.Name == "set");

        // ── 2. Assemble：AI 用最简 DSL 草稿装配工作流 ──
        var assembleRequest = new AssembleWorkflowRequest
        {
            Name = "AI E2E Test Workflow",
            Nodes =
            [
                new AiDraftNodeDto
                {
                    Id = "trigger",
                    TypeName = "manualTrigger",
                    Parameters = new Dictionary<string, object>(),
                },
                new AiDraftNodeDto
                {
                    Id = "set",
                    TypeName = "set",
                    Parameters = new Dictionary<string, object>
                    {
                        ["fields"] = JsonSerializer.SerializeToNode(
                            new[] { new { name = "greeting", value = "hello" } },
                            JsonDefaults.Options)!,
                        ["include"] = "All",
                    },
                },
            ],
            Connections =
            [
                new AiDraftConnectionDto
                {
                    From = "trigger",
                    To = "set",
                },
            ],
        };

        var assembleResponse = await client.PostAsJsonAsync("/api/v1/workflows/assemble", assembleRequest, ct);
        Assert.Equal(HttpStatusCode.Created, assembleResponse.StatusCode);
        var assembleResult = await assembleResponse.Content.ReadFromJsonAsync<AssembleWorkflowResult>(TestJsonOptions, ct);
        Assert.NotNull(assembleResult);
        Assert.NotEqual(Guid.Empty, assembleResult!.DraftId);
        Assert.True(assembleResult.Workflow.Nodes.Count == 2);
        Assert.True(assembleResult.Workflow.Connections.Count == 1);

        // ── 3. Confirm：激活草稿 ──
        var confirmResponse = await client.PostAsync($"/api/v1/workflows/{assembleResult.DraftId}/confirm", null, ct);
        Assert.Equal(HttpStatusCode.OK, confirmResponse.StatusCode);
        var confirmedWorkflow = await confirmResponse.Content.ReadFromJsonAsync<WorkflowDto>(TestJsonOptions, ct);
        Assert.NotNull(confirmedWorkflow);
        Assert.True(confirmedWorkflow!.IsActive);

        // ── 4. Execute：触发工作流执行 ──
        var executeResponse = await client.PostAsync($"/api/v1/workflows/{confirmedWorkflow.Id}/execute", null, ct);
        Assert.Equal(HttpStatusCode.OK, executeResponse.StatusCode);
        var execution = await executeResponse.Content.ReadFromJsonAsync<ExecutionDto>(TestJsonOptions, ct);
        Assert.NotNull(execution);
        Assert.Equal(confirmedWorkflow.Id, execution!.WorkflowDefinitionId);

        // ── 5. 验证执行已成功创建 ──
        // 测试工厂移除了后台执行 worker，执行记录停留在 Pending（不会推进到 Running/Completed）。
        // 节点实际执行已由 dry-run / 运行时测试覆盖，此处仅验证 execute 端点成功创建执行记录。
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FlowEngineDbContext>();
        var record = await dbContext.ExecutionRecords.FirstOrDefaultAsync(e => e.Id == execution.Id, ct);
        Assert.NotNull(record);
        Assert.Equal(confirmedWorkflow.Id, record!.WorkflowDefinitionId);
        Assert.Equal(ExecutionStatus.Pending, record.Status);
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync(
        string email,
        IReadOnlyList<string>? roles = null,
        CancellationToken ct = default)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FlowEngineDbContext>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();

        var user = new User
        {
            Email = email,
            UserName = email.Split('@')[0],
            DisplayName = email,
            PasswordHash = passwordHasher.HashPassword("StrongP@ss1"),
            IsActive = true,
        };
        dbContext.Set<User>().Add(user);
        await dbContext.SaveChangesAsync(ct);

        if (roles is not null)
        {
            foreach (var role in roles)
            {
                dbContext.Set<UserRole>().Add(new UserRole { UserId = user.Id, Role = role });
            }

            await dbContext.SaveChangesAsync(ct);
        }

        var token = tokenService.GenerateAccessToken(user.Id, user.Email, roles ?? []);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static JsonSerializerOptions TestJsonOptions => new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };
}

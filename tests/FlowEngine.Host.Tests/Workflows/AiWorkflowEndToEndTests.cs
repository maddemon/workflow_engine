using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FlowEngine.Application.Dtos;
using FlowEngine.Core;
using FlowEngine.Core.Ai;
using FlowEngine.Core.Authorization;
using FlowEngine.Core.Data;
using FlowEngine.Core.Enums;
using FlowEngine.Host.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FlowEngine.Host.Tests.Workflows;

/// <summary>
/// AI 工作流端到端集成测试：Catalog → assemble → confirm → execute。
/// 验证 AI-native API 全链路可成功创建并执行工作流。
/// </summary>
public class AiWorkflowEndToEndTests : HostIntegrationTestBase
{
    public AiWorkflowEndToEndTests(FlowEngineWebApplicationFactory factory)
        : base(factory, builder =>
        {
            builder.UseSetting("ExecutionCleanup:Enabled", "false");
        })
    {
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

    /// <summary>
    /// AI 自纠循环集成测试：validate → fix → re-validate → assemble → confirm → execute。
    /// 模拟 AI 收到校验错误后自动修复参数值，再继续走完整装配执行链路。
    /// </summary>
    [Fact]
    public async Task AiWorkflow_Validate_Fix_Revalidate_AssembleExecute_Succeeds()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await CreateAuthenticatedClientAsync("ai-fix-e2e@example.com", [RoleConstants.Admin], ct);

        // ── 1. Validate：发送含 Options 类型参数非法值的请求 ──
        var validateRequest = new ValidateWorkflowRequest
        {
            Nodes =
            [
                new NodeDefinitionDto
                {
                    Id = "trigger",
                    TypeName = "manualTrigger",
                    Parameters = new Dictionary<string, object>(),
                },
                new NodeDefinitionDto
                {
                    Id = "set",
                    TypeName = "set",
                    Parameters = new Dictionary<string, object>
                    {
                        ["include"] = "InvalidMode",
                    },
                },
            ],
            Connections =
            [
                new ConnectionDto
                {
                    Id = "c1",
                    SourceNodeId = "trigger",
                    TargetNodeId = "set",
                },
            ],
        };

        var validateResponse = await client.PostAsJsonAsync("/api/v1/workflows/validate", validateRequest, ct);
        Assert.Equal(HttpStatusCode.OK, validateResponse.StatusCode);
        var result = await validateResponse.Content.ReadFromJsonAsync<ValidateWorkflowResult>(TestJsonOptions, ct);
        Assert.NotNull(result);

        // 2a. 校验失败，返回 InvalidValue 错误
        Assert.False(result!.Valid);
        Assert.Contains(result.Errors, e =>
            e.NodeId == "set"
            && e.Field == "include"
            && e.ErrorType == "InvalidValue"
            && e.Message.Contains("InvalidMode")
            && e.SuggestedFix != null
            && e.SuggestedFix.Contains("All"));
        Assert.True(result.CanAutoFix, "InvalidValue 错误应标记为可自动修复");

        // ── 3. Fix：将 include 修复为合法值 "All" ──
        validateRequest.Nodes![1].Parameters["include"] = "All";

        // ── 4. Re-validate ──
        var revalidateResponse = await client.PostAsJsonAsync("/api/v1/workflows/validate", validateRequest, ct);
        Assert.Equal(HttpStatusCode.OK, revalidateResponse.StatusCode);
        var fixedResult = await revalidateResponse.Content.ReadFromJsonAsync<ValidateWorkflowResult>(TestJsonOptions, ct);
        Assert.NotNull(fixedResult);
        Assert.True(fixedResult!.Valid, "修复后校验应通过");
        Assert.Empty(fixedResult.Errors);

        // ── 5. Assemble：用修正后的参数装配工作流 ──
        var assembleRequest = new AssembleWorkflowRequest
        {
            Name = "AI Auto-Fix E2E Test",
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

        // ── 6. Confirm ──
        var confirmResponse = await client.PostAsync($"/api/v1/workflows/{assembleResult.DraftId}/confirm", null, ct);
        Assert.Equal(HttpStatusCode.OK, confirmResponse.StatusCode);
        var confirmedWorkflow = await confirmResponse.Content.ReadFromJsonAsync<WorkflowDto>(TestJsonOptions, ct);
        Assert.NotNull(confirmedWorkflow);
        Assert.True(confirmedWorkflow!.IsActive);

        // ── 7. Execute ──
        var executeResponse = await client.PostAsync($"/api/v1/workflows/{confirmedWorkflow.Id}/execute", null, ct);
        Assert.Equal(HttpStatusCode.OK, executeResponse.StatusCode);
        var execution = await executeResponse.Content.ReadFromJsonAsync<ExecutionDto>(TestJsonOptions, ct);
        Assert.NotNull(execution);
        Assert.Equal(confirmedWorkflow.Id, execution!.WorkflowDefinitionId);

        // ── 8. 验证执行记录 ──
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FlowEngineDbContext>();
        var record = await dbContext.ExecutionRecords.FirstOrDefaultAsync(e => e.Id == execution.Id, ct);
        Assert.NotNull(record);
        Assert.Equal(confirmedWorkflow.Id, record!.WorkflowDefinitionId);
        Assert.Equal(ExecutionStatus.Pending, record.Status);
    }

    /// <summary>
    /// Modify 端到端集成测试：assemble → confirm → modify → confirm draft → execute。
    /// 模拟 AI 对已有工作流添加新节点并调整参数。
    /// </summary>
    [Fact]
    public async Task AiWorkflow_Modify_EndToEnd_Succeeds()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await CreateAuthenticatedClientAsync("ai-mod-e2e@example.com", [RoleConstants.Admin], ct);

        // ── 1. Assemble：创建基础工作流 ──
        var assembleRequest = new AssembleWorkflowRequest
        {
            Name = "AI Modify E2E Test",
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
        var workflowId = assembleResult!.DraftId;

        // ── 2. Confirm ──
        var confirmResponse = await client.PostAsync($"/api/v1/workflows/{workflowId}/confirm", null, ct);
        Assert.Equal(HttpStatusCode.OK, confirmResponse.StatusCode);
        var confirmedWorkflow = await confirmResponse.Content.ReadFromJsonAsync<WorkflowDto>(TestJsonOptions, ct);
        Assert.NotNull(confirmedWorkflow);
        Assert.True(confirmedWorkflow!.IsActive);

        // ── 3. Modify：添加第二个 set 节点并修改 include 参数 ──
        var modifyRequest = new ModifyWorkflowRequest
        {
            Operations =
            [
                new WorkflowOperation
                {
                    Op = "modify",
                    Path = "/nodes/set/parameters/include",
                    Value = "Selected",
                },
                new WorkflowOperation
                {
                    Op = "add",
                    Node = new AiDraftNodeDto
                    {
                        Id = "set2",
                        TypeName = "set",
                        Parameters = new Dictionary<string, object>
                        {
                            ["fields"] = JsonSerializer.SerializeToNode(
                                new[] { new { name = "fullName", value = "hello world" } },
                                JsonDefaults.Options)!,
                            ["include"] = "All",
                        },
                    },
                    After = "set",
                },
            ],
        };

        var modifyResponse = await client.PostAsJsonAsync(
            $"/api/v1/workflows/{confirmedWorkflow.Id}/modify", modifyRequest, ct);
        Assert.Equal(HttpStatusCode.OK, modifyResponse.StatusCode);
        var modifyResult = await modifyResponse.Content.ReadFromJsonAsync<ModifyWorkflowResult>(TestJsonOptions, ct);
        Assert.NotNull(modifyResult);
        Assert.NotEqual(Guid.Empty, modifyResult!.DraftId);
        Assert.Equal(3, modifyResult.Workflow.Nodes.Count); // trigger + set + set2
        Assert.Contains(modifyResult.Diff, d => d.Op == "modify" && d.NodeId == "set");
        Assert.Contains(modifyResult.Diff, d => d.Op == "add" && d.NodeId == "set2");

        // ── 4. Confirm 草稿 ──
        var confirmDraftResponse = await client.PostAsync(
            $"/api/v1/workflows/{modifyResult.DraftId}/confirm", null, ct);
        Assert.Equal(HttpStatusCode.OK, confirmDraftResponse.StatusCode);
        var modifiedWorkflow = await confirmDraftResponse.Content.ReadFromJsonAsync<WorkflowDto>(TestJsonOptions, ct);
        Assert.NotNull(modifiedWorkflow);
        Assert.True(modifiedWorkflow!.IsActive);
        Assert.Equal(3, modifiedWorkflow.Nodes.Count);

        // ── 5. Execute ──
        var executeResponse = await client.PostAsync(
            $"/api/v1/workflows/{modifiedWorkflow.Id}/execute", null, ct);
        Assert.Equal(HttpStatusCode.OK, executeResponse.StatusCode);
        var execution = await executeResponse.Content.ReadFromJsonAsync<ExecutionDto>(TestJsonOptions, ct);
        Assert.NotNull(execution);
        Assert.Equal(modifiedWorkflow.Id, execution!.WorkflowDefinitionId);

        // ── 6. 验证执行记录 ──
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FlowEngineDbContext>();
        var record = await dbContext.ExecutionRecords.FirstOrDefaultAsync(e => e.Id == execution.Id, ct);
        Assert.NotNull(record);
        Assert.Equal(modifiedWorkflow.Id, record!.WorkflowDefinitionId);
        Assert.Equal(ExecutionStatus.Pending, record.Status);
    }

}

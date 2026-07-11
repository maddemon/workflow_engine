using FlowEngine.Application.Dtos;
using FlowEngine.Application.Workflows;
using FlowEngine.Core.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlowEngine.Host.Controllers;

/// <summary>
/// 工作流 CRUD API。
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/workflows")]
public class WorkflowsController(
    WorkflowService workflowService,
    WorkflowExportService exportService,
    WorkflowImportService importService,
    WorkflowDryRunService dryRunService,
    WorkflowGenerationService generationService) : ControllerBase
{
    /// <summary>
    /// 分页获取工作流摘要列表。
    /// </summary>
    [HttpGet]
    [AuthorizePermission(Scope.Workflow, Operation.Read)]
    public async Task<ActionResult<PagedResult<WorkflowSummaryDto>>> GetAll(
        [FromQuery] Guid? projectId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await workflowService.GetAllAsync(projectId, page, pageSize, cancellationToken).ConfigureAwait(false);
        return Ok(result);
    }

    /// <summary>
    /// 按 ID 获取最新版本工作流。
    /// </summary>
    [HttpGet("{id:guid}")]
    [AuthorizePermission(Scope.Workflow, Operation.Read)]
    public async Task<ActionResult<WorkflowDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var workflow = await workflowService.GetAsync(id, cancellationToken).ConfigureAwait(false);
        return this.OkOrNotFound(workflow);
    }

    /// <summary>
    /// 创建工作流。
    /// </summary>
    [HttpPost]
    [AuthorizePermission(Scope.Workflow, Operation.Write)]
    public async Task<ActionResult<WorkflowDto>> Create(
        [FromBody] CreateWorkflowDto workflow,
        CancellationToken cancellationToken)
    {
        var result = await workflowService.CreateAsync(workflow, cancellationToken).ConfigureAwait(false);
        return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    }

    /// <summary>
    /// 更新工作流并递增版本号。
    /// </summary>
    [HttpPut("{id:guid}")]
    [AuthorizePermission(Scope.Workflow, Operation.Write)]
    public async Task<ActionResult<WorkflowDto>> Update(
        Guid id,
        [FromBody] UpdateWorkflowDto workflow,
        CancellationToken cancellationToken)
    {
        var result = await workflowService.UpdateAsync(id, workflow, cancellationToken).ConfigureAwait(false);
        return this.OkOrNotFound(result);
    }

    /// <summary>
    /// 删除工作流。
    /// </summary>
    [HttpDelete("{id:guid}")]
    [AuthorizePermission(Scope.Workflow, Operation.Delete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var deleted = await workflowService.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
        if (!deleted)
        {
            return NotFound();
        }

        return NoContent();
    }

    /// <summary>
    /// 在无副作用模式下预演工作流执行。
    /// </summary>
    [HttpPost("dry-run")]
    [Authorize]
    public async Task<ActionResult<ExecutionDto>> DryRun(
        [FromBody] DryRunWorkflowRequestDto request,
        CancellationToken cancellationToken)
    {
        if (request.Nodes is null || request.Nodes.Count == 0)
        {
            return this.BadRequestError("Nodes 不能为空。");
        }

        if (request.Connections is null || request.Connections.Count == 0)
        {
            return this.BadRequestError("Connections 不能为空。");
        }

        var result = await dryRunService.DryRunAsync(request, cancellationToken).ConfigureAwait(false);
        return Ok(result);
    }

    /// <summary>
    /// 获取工作流的所有历史版本号。
    /// </summary>
    [HttpGet("{id:guid}/versions")]
    [AuthorizePermission(Scope.Workflow, Operation.Read)]
    public async Task<ActionResult<IReadOnlyCollection<int>>> GetVersions(
        Guid id,
        CancellationToken cancellationToken)
    {
        var versions = await workflowService.GetVersionsAsync(id, cancellationToken).ConfigureAwait(false);
        return Ok(versions);
    }

    /// <summary>
    /// 按版本号获取工作流。
    /// </summary>
    [HttpGet("{id:guid}/versions/{version:int}")]
    [AuthorizePermission(Scope.Workflow, Operation.Read)]
    public async Task<ActionResult<WorkflowDto>> GetVersion(
        Guid id,
        int version,
        CancellationToken cancellationToken)
    {
        var workflow = await workflowService.GetVersionAsync(id, version, cancellationToken)
            .ConfigureAwait(false);
        return this.OkOrNotFound(workflow);
    }

    /// <summary>
    /// 导出工作流为 JSON。
    /// </summary>
    [HttpGet("{id:guid}/export")]
    [AuthorizePermission(Scope.Workflow, Operation.Read)]
    public async Task<ActionResult<WorkflowExportResult>> Export(
        Guid id,
        CancellationToken cancellationToken)
    {
        var exportedBy = User.Identity?.Name ?? "unknown";
        var json = await exportService.ExportAsync(id, exportedBy, cancellationToken).ConfigureAwait(false);
        var result = System.Text.Json.JsonSerializer.Deserialize<WorkflowExportResult>(json);
        return this.OkOrNotFound(result);
    }

    /// <summary>
    /// 批量导出工作流为 JSON 数组。
    /// </summary>
    [HttpPost("export-batch")]
    [AuthorizePermission(Scope.Workflow, Operation.Read)]
    public async Task<ActionResult<List<WorkflowExportResult>>> ExportBatch(
        [FromBody] ExportBatchRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Ids is null || request.Ids.Count == 0)
        {
            return this.BadRequestError("工作流 ID 列表不能为空。");
        }

        var exportedBy = User.Identity?.Name ?? "unknown";
        try
        {
            var json = await exportService.ExportBatchAsync(request.Ids, exportedBy, cancellationToken)
                .ConfigureAwait(false);
            var results = System.Text.Json.JsonSerializer.Deserialize<List<WorkflowExportResult>>(json);
            return this.OkOrNotFound(results);
        }
        catch (InvalidOperationException ex)
        {
            return this.BadRequestError(ex.Message);
        }
    }

    /// <summary>
    /// 导入工作流。
    /// </summary>
    [HttpPost("import")]
    [AuthorizePermission(Scope.Workflow, Operation.Write)]
    public async Task<ActionResult<ImportResult>> Import(
        [FromBody] ImportWorkflowRequest request,
        CancellationToken cancellationToken)
    {
        var result = await importService.ImportAsync(
            request.Json,
            request.ProjectId,
            request.ImportedBy,
            cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
            return BadRequest(result);
        }

        return Ok(result);
    }

    /// <summary>
    /// 批量导入工作流。
    /// </summary>
    [HttpPost("import-batch")]
    [AuthorizePermission(Scope.Workflow, Operation.Write)]
    public async Task<ActionResult<BatchImportResult>> ImportBatch(
        [FromBody] ImportBatchRequest request,
        CancellationToken cancellationToken)
    {
        var result = await importService.ImportBatchAsync(
            request.Json,
            request.ProjectId,
            request.ImportedBy,
            cancellationToken).ConfigureAwait(false);

        if (result.FailureCount > 0 && result.SuccessCount == 0)
        {
            return BadRequest(result);
        }

        return Ok(result);
    }

    /// <summary>
    /// 根据自然语言描述生成工作流草案（AI 语义解析 + 结构化校验-纠错循环）。
    /// 返回的 <c>valid=true</c> 表示草案通过校验；<c>valid=false</c> 时附带错误清单与最后一次草案。
    /// </summary>
    [HttpPost("generate")]
    [AuthorizePermission(Scope.Workflow, Operation.Write)]
    public async Task<ActionResult<WorkflowGenerationResponse>> Generate(
        [FromBody] WorkflowGenerationRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return this.BadRequestError("请求体不能为空。");
        }

        var result = await generationService.GenerateAsync(request, cancellationToken).ConfigureAwait(false);
        return Ok(result);
    }
}

using System.Text.Json;
using FlowEngine.Application.Dtos;
using FlowEngine.Application.Workflows;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace FlowEngine.Application.Executions;

/// <summary>
/// 执行应用服务，编排工作流执行与查询。
/// </summary>
public sealed class ExecutionService
{
    private readonly IEngine _engine;
    private readonly FlowEngineDbContext _dbContext;
    private readonly WorkflowService _workflowService;
    private readonly IExecutionIdempotencyService _idempotencyService;

    /// <summary>
    /// 初始化执行应用服务。
    /// </summary>
    public ExecutionService(
        IEngine engine,
        FlowEngineDbContext dbContext,
        WorkflowService workflowService,
        IExecutionIdempotencyService idempotencyService)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _workflowService = workflowService ?? throw new ArgumentNullException(nameof(workflowService));
        _idempotencyService = idempotencyService;
    }

    /// <summary>
    /// 启动工作流执行。
    /// </summary>
    public async Task<ExecutionDto?> ExecuteAsync(Guid workflowId, string? idempotencyKey = null, CancellationToken cancellationToken = default)
    {
        var workflow = await _workflowService.GetAsync(workflowId, cancellationToken).ConfigureAwait(false);
        if (workflow is null)
        {
            return null;
        }

        // 幂等检查：如果提供了幂等键，检查是否已存在
        if (!string.IsNullOrEmpty(idempotencyKey))
        {
            var tempExecutionId = Guid.NewGuid();
            var existingExecutionId = await _idempotencyService.TryGetOrRegisterAsync(
                idempotencyKey, tempExecutionId, TimeSpan.FromSeconds(3600), cancellationToken).ConfigureAwait(false);
            if (existingExecutionId.HasValue)
            {
                // 返回已存在的执行记录
                var existingRecord = await _dbContext.ExecutionRecords
                    .FirstOrDefaultAsync(e => e.Id == existingExecutionId.Value, cancellationToken)
                    .ConfigureAwait(false);
                return existingRecord is not null ? MapToDto(existingRecord) : new ExecutionDto
                {
                    Id = existingExecutionId.Value,
                    WorkflowDefinitionId = workflowId,
                    Status = "Idempotent",
                    StartedAt = DateTime.UtcNow
                };
            }
        }

        var executionId = await _engine.StartAsync(workflowId, null, cancellationToken).ConfigureAwait(false);

        // 更新幂等记录的 ExecutionId 为实际值
        if (!string.IsNullOrEmpty(idempotencyKey))
        {
            var dedupRecord = await _dbContext.ExecutionDedups
                .FirstOrDefaultAsync(e => e.IdempotencyKey == idempotencyKey, cancellationToken)
                .ConfigureAwait(false);
            if (dedupRecord is not null && dedupRecord.ExecutionId != executionId.Value)
            {
                dedupRecord.ExecutionId = executionId.Value;
                await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        var record = await _dbContext.ExecutionRecords
            .FirstOrDefaultAsync(e => e.Id == executionId.Value, cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            return new ExecutionDto
            {
                Id = executionId.Value,
                WorkflowDefinitionId = workflowId,
                Status = ExecutionStatus.Pending.ToString(),
                StartedAt = DateTime.UtcNow
            };
        }

        return MapToDto(record);
    }

    /// <summary>
    /// 按 ID 获取执行详情。
    /// </summary>
    public async Task<ExecutionDto?> GetAsync(Guid executionId, CancellationToken cancellationToken = default)
    {
        var record = await _dbContext.ExecutionRecords
            .FirstOrDefaultAsync(e => e.Id == executionId, cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            return null;
        }

        return MapToDto(record);
    }

    /// <summary>
    /// 按工作流定义 ID 获取执行列表。
    /// </summary>
    public async Task<IReadOnlyCollection<ExecutionSummaryDto>> GetByWorkflowAsync(
        Guid workflowId,
        CancellationToken cancellationToken = default)
    {
        var records = await _dbContext.ExecutionRecords
            .Where(e => e.WorkflowDefinitionId == workflowId)
            .OrderByDescending(e => e.StartedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return records.Select(MapToSummary).ToList();
    }

    private static ExecutionDto MapToDto(Core.Entities.ExecutionRecord record)
    {
        return new ExecutionDto
        {
            Id = record.Id,
            WorkflowDefinitionId = record.WorkflowDefinitionId,
            Status = record.Status.ToString(),
            StartedAt = record.StartedAt,
            CompletedAt = record.CompletedAt,
            NodeRecords = record.NodeRecords.Select(MapToNodeRecord).ToList()
        };
    }

    private static NodeExecutionRecordDto MapToNodeRecord(Core.Entities.NodeExecutionRecord node)
    {
        return new NodeExecutionRecordDto
        {
            Id = node.Id,
            NodeDefinitionId = node.NodeDefinitionId,
            RunIndex = node.RunIndex,
            Status = node.Output.Success ? "Completed" : "Failed",
            StartedAt = node.StartedAt ?? default,
            CompletedAt = node.CompletedAt,
            Inputs = SerializeInputs(node.Inputs),
            Output = node.Output is null ? null : JsonSerializer.SerializeToNode(node.Output, JsonDefaults.Options),
            RawParameters = SerializeToDictionary(node.RawParameters),
            ResolvedParameters = SerializeToDictionary(node.ResolvedParameters)
        };
    }

    private static ExecutionSummaryDto MapToSummary(Core.Entities.ExecutionRecord record)
    {
        return new ExecutionSummaryDto
        {
            Id = record.Id,
            WorkflowDefinitionId = record.WorkflowDefinitionId,
            Status = record.Status.ToString(),
            StartedAt = record.StartedAt,
            CompletedAt = record.CompletedAt
        };
    }

    private static Dictionary<string, object>? SerializeInputs(IReadOnlyDictionary<string, Core.Entities.DataBatch>? inputs)
    {
        if (inputs is null || inputs.Count == 0)
        {
            return null;
        }

        var result = new Dictionary<string, object>(inputs.Count);
        foreach (var (key, value) in inputs)
        {
            result[key] = JsonSerializer.SerializeToNode(value, JsonDefaults.Options) ?? string.Empty;
        }

        return result;
    }

    private static Dictionary<string, object>? SerializeToDictionary<TKey>(IReadOnlyDictionary<TKey, object>? dict)
        where TKey : notnull
    {
        if (dict is null || dict.Count == 0)
        {
            return null;
        }

        var result = new Dictionary<string, object>(dict.Count);
        foreach (var (key, value) in dict)
        {
            result[key.ToString()!] = value is string or int or long or double or float or decimal or bool or DateTime
                ? value
                : JsonSerializer.SerializeToNode(value, JsonDefaults.Options) ?? string.Empty;
        }

        return result;
    }
}

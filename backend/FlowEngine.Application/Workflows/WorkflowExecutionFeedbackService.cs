using FlowEngine.Application.Dtos;
using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace FlowEngine.Application.Workflows;

/// <summary>
/// 执行反馈服务——分析执行结果，提供结构化反馈以支持 AI 自动修复。
/// Phase 2 为基础实现，后续阶段将扩展为完整的错误分析引擎。
/// </summary>
public sealed class WorkflowExecutionFeedbackService(
    FlowEngineDbContext dbContext) : IWorkflowExecutionFeedbackService
{
    /// <summary>
    /// 获取执行反馈。
    /// </summary>
    /// <param name="executionId">执行记录 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>执行反馈结果；执行不存在时返回 null。</returns>
    public async Task<ExecutionFeedbackResult?> GetFeedbackAsync(
        Guid executionId,
        CancellationToken cancellationToken = default)
    {
        var record = await dbContext.ExecutionRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == executionId, cancellationToken)
            .ConfigureAwait(false);

        if (record is null)
        {
            return null;
        }

        // CQ-7：从工作流定义解析节点显示名与类型名（NodeExecutionRecord 本身不含 Name/TypeName）。
        var nodeDefById = new Dictionary<string, NodeDefinition>(StringComparer.Ordinal);
        var workflow = await dbContext.Workflows
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == record.WorkflowDefinitionId, cancellationToken)
            .ConfigureAwait(false);
        if (workflow is not null)
        {
            foreach (var node in workflow.Nodes)
            {
                nodeDefById[node.Id] = node;
            }
        }

        var succeeded = record.Status == ExecutionStatus.Completed
                        || record.Status == ExecutionStatus.DryRunCompleted;

        var feedbackNodes = new List<ExecutionFeedbackNode>();
        var canAutoFix = true;

        if (record.NodeRecords is not null)
        {
            foreach (var nodeRecord in record.NodeRecords)
            {
                var isFailed = nodeRecord.Output?.Success == false;
                nodeDefById.TryGetValue(nodeRecord.NodeDefinitionId, out var nodeDef);
                var nodeFeedback = new ExecutionFeedbackNode
                {
                    NodeId = nodeRecord.NodeDefinitionId,
                    // 优先使用工作流定义中的节点显示名；找不到定义时回退为节点定义 ID。
                    NodeName = nodeDef is not null && !string.IsNullOrEmpty(nodeDef.Name)
                        ? nodeDef.Name
                        : nodeRecord.NodeDefinitionId,
                    TypeName = nodeDef?.TypeName ?? string.Empty,
                    Status = isFailed ? "Failed" : "Completed",
                    ErrorType = isFailed ? "ExecutionError" : null,
                    ErrorMessage = isFailed ? (nodeRecord.Output?.Error?.Message ?? "未知错误") : null,
                    SuggestedFix = isFailed ? SuggestFix(nodeRecord) : null,
                    ExecutionContext = BuildExecutionContext(nodeRecord),
                };

                if (isFailed && string.IsNullOrEmpty(nodeFeedback.SuggestedFix))
                {
                    canAutoFix = false;
                }

                feedbackNodes.Add(nodeFeedback);
            }
        }

        return new ExecutionFeedbackResult
        {
            Success = succeeded,
            Nodes = feedbackNodes,
            CanAutoFix = canAutoFix,
        };
    }

    /// <summary>
    /// 构建执行上下文，供 AI 自纠参考。
    /// </summary>
    private static object? BuildExecutionContext(NodeExecutionRecord record)
    {
        // 提供节点原始参数与输入数据批次，使 AI 能据此定位参数错误、表达式引用缺失等问题。
        return new
        {
            RawParameters = record.RawParameters,
            Inputs = record.Inputs,
        };
    }

    /// <summary>
    /// 根据节点执行记录尝试推断修复建议。
    /// </summary>
    private static string? SuggestFix(NodeExecutionRecord record)
    {
        if (record.Output?.Error is null)
        {
            return null;
        }

        var errorMessage = record.Output.Error.Message ?? string.Empty;

        // 常见错误模式匹配
        if (errorMessage.Contains("timeout", StringComparison.OrdinalIgnoreCase)
            || errorMessage.Contains("timed out", StringComparison.OrdinalIgnoreCase))
        {
            return "节点执行超时，请增加 Timeout 设置或优化执行逻辑。";
        }

        if (errorMessage.Contains("connection", StringComparison.OrdinalIgnoreCase)
            && (errorMessage.Contains("refused", StringComparison.OrdinalIgnoreCase)
                || errorMessage.Contains("failed", StringComparison.OrdinalIgnoreCase)))
        {
            return "连接被拒绝，请检查目标服务是否可用以及 URL 配置是否正确。";
        }

        if (errorMessage.Contains("unauthorized", StringComparison.OrdinalIgnoreCase)
            || errorMessage.Contains("401", StringComparison.OrdinalIgnoreCase)
            || errorMessage.Contains("403", StringComparison.OrdinalIgnoreCase))
        {
            return "认证失败，请检查凭据配置。";
        }

        if (errorMessage.Contains("not found", StringComparison.OrdinalIgnoreCase)
            || errorMessage.Contains("404", StringComparison.OrdinalIgnoreCase))
        {
            return "资源未找到，请检查请求 URL 或参数配置。";
        }

        return null;
    }
}

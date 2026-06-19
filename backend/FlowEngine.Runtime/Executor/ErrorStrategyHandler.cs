using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;

namespace FlowEngine.Runtime.Executor;

/// <summary>
/// 错误策略处理。
/// </summary>
public sealed class ErrorStrategyHandler
{
    /// <summary>
    /// 根据错误策略处理节点执行失败结果。
    /// </summary>
    /// <param name="result">节点执行结果。</param>
    /// <param name="nodeDefinitionId">节点定义 ID。</param>
    /// <param name="strategy">错误策略。</param>
    /// <returns>处理后的结果。</returns>
    public NodeExecutionResult Handle(
        NodeExecutionResult result,
        Guid nodeDefinitionId,
        ErrorStrategy strategy)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (strategy == ErrorStrategy.Continue)
        {
            return CreateContinueResult(result, nodeDefinitionId);
        }

        return result;
    }

    /// <summary>
    /// 创建输入超时的失败结果。
    /// </summary>
    public NodeExecutionResult CreateInputTimeoutResult(Guid nodeDefinitionId)
    {
        return new NodeExecutionResult
        {
            Success = false,
            Output = new DataBatch
            {
                Items =
                [
                    new DataItem
                    {
                        Success = false,
                        Error = new NodeError
                        {
                            Code = "InputTimeout",
                            Message = "等待输入超时。",
                            NodeDefinitionId = nodeDefinitionId
                        }
                    }
                ]
            }
        };
    }

    private static NodeExecutionResult CreateContinueResult(NodeExecutionResult original, Guid nodeDefinitionId)
    {
        var error = original.Error ?? new NodeError
        {
            Code = "NodeError",
            Message = "节点执行失败。",
            NodeDefinitionId = nodeDefinitionId
        };

        return new NodeExecutionResult
        {
            Success = false,
            Output = new DataBatch
            {
                Items =
                [
                    new DataItem
                    {
                        Success = false,
                        Error = error,
                        Data = original.Output?.Items.FirstOrDefault()?.Data
                    }
                ]
            },
            BranchIndex = original.BranchIndex
        };
    }
}

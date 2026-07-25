using System;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Configuration;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Scripting;
using FlowEngine.Core.ValueObjects;
using Microsoft.Extensions.Logging;

namespace FlowEngine.Runtime.Executor;

/// <summary>
/// 节点执行重试与超时控制：从 <see cref="WorkflowSchedulerKernel"/> 抽离的单一职责协作者。
/// 负责包裹节点执行，处理超时、取消、异常，并按退避策略对可重试错误进行重试。
/// </summary>
public sealed class RetryExecutor
{
    private readonly EngineDefaultsOptions _defaults;
    private readonly ErrorStrategyHandler _errorHandler;
    private readonly ILogger _logger;

    /// <summary>
    /// 构造重试执行器。
    /// </summary>
    /// <param name="defaults">引擎默认配置（默认最大重试、默认超时、默认退避上下限）。</param>
    /// <param name="errorHandler">错误策略处理（Continue 策略下对失败结果做安全包装）。</param>
    /// <param name="logger">日志。</param>
    public RetryExecutor(EngineDefaultsOptions defaults, ErrorStrategyHandler errorHandler, ILogger logger)
    {
        _defaults = defaults;
        _errorHandler = errorHandler;
        _logger = logger;
    }

    /// <summary>
    /// 带重试执行节点：处理超时、取消、异常，并按退避策略重试可重试错误。
    /// 超时（节点自身超时而非外部取消）不重试，直接返回超时错误；外部取消不重试，
    /// 由上层错误策略处理。
    /// </summary>
    /// <param name="node">节点定义。</param>
    /// <param name="nodeType">节点类型实例。</param>
    /// <param name="context">节点执行上下文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>节点执行结果。</returns>
    public async Task<NodeExecutionResult> ExecuteNodeWithRetryAsync(
        NodeDefinition node,
        INodeType nodeType,
        NodeExecutionContext context,
        CancellationToken cancellationToken)
    {
        var maxRetries = node.RetryPolicy?.MaxRetries
            ?? (node.ErrorStrategy == ErrorStrategy.Retry ? Math.Max(_defaults.DefaultMaxRetries, 1) : _defaults.DefaultMaxRetries);

        var effectiveTimeout = node.Timeout
            ?? (_defaults.DefaultTimeoutSeconds.HasValue ? TimeSpan.FromSeconds(_defaults.DefaultTimeoutSeconds.Value) : null);

        NodeExecutionResult result;
        for (var attempt = 0; attempt <= maxRetries; attempt++)
        {
            CancellationTokenSource? timeoutCts = null;
            try
            {
                var effectiveToken = cancellationToken;
                if (effectiveTimeout is { } timeout && timeout > TimeSpan.Zero)
                {
                    timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeoutCts.CancelAfter(timeout);
                    effectiveToken = timeoutCts.Token;
                }

                result = await nodeType.ExecuteAsync(context, effectiveToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timeoutCts is not null && timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                // 节点超时，不重试，直接返回超时错误。
                var timeoutError = new NodeError
                {
                    Code = "Timeout",
                    Message = $"节点执行超时，超时时间：{effectiveTimeout!.Value.TotalMilliseconds}ms。",
                    NodeDefinitionId = node.Id
                };
                return new NodeExecutionResult
                {
                    Success = false,
                    Error = timeoutError,
                    Output = new DataBatch
                    {
                        Items =
                        [
                            new DataItem
                            {
                                Success = false,
                                Error = timeoutError
                            }
                        ]
                    }
                };
            }
            catch (OperationCanceledException)
            {
                // 取消异常不重试，直接返回取消结果由上层错误策略处理。
                var cancelledError = new NodeError
                {
                    Code = "Cancelled",
                    Message = "节点执行被取消。",
                    NodeDefinitionId = node.Id
                };
                return new NodeExecutionResult
                {
                    Success = false,
                    Error = cancelledError,
                    Output = new DataBatch
                    {
                        Items =
                        [
                            new DataItem
                            {
                                Success = false,
                                Error = cancelledError
                            }
                        ]
                    }
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "节点 {NodeName} ({NodeId}) 执行时发生异常。", node.Name, node.Id);
                // EX-2：仅向客户端暴露安全错误码与脱敏消息，绝不泄露原始异常文本或堆栈。
                var nodeError = NodeErrorFactory.Sanitize(ex, "NodeExecutionFailed", node.Id);
                result = new NodeExecutionResult
                {
                    Success = false,
                    Error = nodeError,
                    Output = new DataBatch
                    {
                        Items =
                        [
                            new DataItem
                            {
                                Success = false,
                                Error = nodeError
                            }
                        ]
                    }
                };
            }
            finally
            {
                timeoutCts?.Dispose();
            }

            // 检查可重试错误码过滤
            if (!result.Success && node.RetryPolicy?.RetryableErrorCodes?.Count > 0)
            {
                var errorCode = result.Error?.Code ?? string.Empty;
                if (!node.RetryPolicy.RetryableErrorCodes.Contains(errorCode))
                {
                    return result; // 错误码不在可重试列表中，直接返回不重试
                }
            }

            if (result.Success || attempt == maxRetries)
            {
                if (!result.Success && node.ErrorStrategy == ErrorStrategy.Continue)
                {
                    return _errorHandler.Handle(result, node.Id, ErrorStrategy.Continue);
                }

                return result;
            }

            var delay = CalculateBackoff(node.RetryPolicy, attempt, _defaults);
            _logger.LogWarning(
                "节点 {NodeName} ({NodeId}) 第 {Attempt} 次执行失败，{Delay}ms 后重试。",
                node.Name,
                node.Id,
                attempt + 1,
                delay.TotalMilliseconds);

            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }

        throw new InvalidOperationException("节点重试逻辑出现不可达路径。");
    }

    /// <summary>
    /// 计算重试退避延迟：按策略（指数/线性/固定）计算，截断于最大延迟，可选抖动。
    /// </summary>
    /// <param name="policy">节点重试策略（为 null 时全部回退默认）。</param>
    /// <param name="attempt">当前重试尝试序号（从 0 起）。</param>
    /// <param name="defaults">引擎默认配置（提供基础/最大延迟回退值）。</param>
    /// <returns>退避延迟。</returns>
    internal static TimeSpan CalculateBackoff(RetryPolicy? policy, int attempt, EngineDefaultsOptions? defaults = null)
    {
        var baseDelay = policy?.BaseDelay > TimeSpan.Zero
            ? policy.BaseDelay
            : TimeSpan.FromSeconds(defaults?.DefaultBaseDelaySeconds ?? 1);
        var maxDelay = policy?.MaxDelay > TimeSpan.Zero
            ? policy.MaxDelay
            : TimeSpan.FromSeconds(defaults?.DefaultMaxDelaySeconds ?? 60);

        var strategy = policy?.BackoffStrategy ?? BackoffStrategy.Exponential;

        TimeSpan delay = strategy switch
        {
            BackoffStrategy.Linear => baseDelay * (attempt + 1),
            BackoffStrategy.Fixed => baseDelay,
            _ => TimeSpan.FromTicks((long)(baseDelay.Ticks * Math.Pow(2, attempt))) // Exponential
        };

        delay = TimeSpan.FromTicks(Math.Min(delay.Ticks, maxDelay.Ticks));

        if (policy?.UseJitter == true)
        {
            var jitter = Random.Shared.NextDouble() * delay.TotalMilliseconds;
            delay = TimeSpan.FromMilliseconds(delay.TotalMilliseconds + jitter);
            delay = TimeSpan.FromTicks(Math.Min(delay.Ticks, maxDelay.Ticks));
        }

        return delay;
    }
}

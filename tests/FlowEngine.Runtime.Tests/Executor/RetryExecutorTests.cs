using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Configuration;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.ValueObjects;
using FlowEngine.Runtime.Executor;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FlowEngine.Runtime.Tests.Executor;

/// <summary>
/// <see cref="RetryExecutor"/> 独立单测：验证重试策略、超时路径、可重试错误码过滤与退避计算。
/// 行为须与从 <see cref="WorkflowSchedulerKernel"/> 抽离前完全一致。
/// </summary>
public sealed class RetryExecutorTests
{
    private readonly RetryExecutor _executor = new(
        new EngineDefaultsOptions(),
        new ErrorStrategyHandler(),
        NullLogger<RetryExecutor>.Instance);

    [Fact]
    public async Task ExecuteNodeWithRetryAsync_RetryableError_RetriesThenSucceeds()
    {
        var node = new FlakyNode { FailCount = 1, ErrorCode = "RetryFailure" };
        var definition = new NodeDefinition
        {
            Id = "n1",
            Name = "n1",
            TypeName = node.TypeName,
            RetryPolicy = new RetryPolicy
            {
                MaxRetries = 3,
                BaseDelay = TimeSpan.Zero,
                RetryableErrorCodes = ["RetryFailure"]
            }
        };
        var context = new NodeExecutionContext { Node = definition, ExecutionId = Guid.NewGuid() };

        var result = await _executor.ExecuteNodeWithRetryAsync(definition, node, context, CancellationToken.None);

        Assert.True(result.Success);
        // 失败后重试一次成功：调用两次。
        Assert.Equal(2, node.Calls);
    }

    [Fact]
    public async Task ExecuteNodeWithRetryAsync_ErrorCodeNotRetryable_StopsWithoutRetry()
    {
        var node = new FlakyNode { FailCount = 1, ErrorCode = "RetryFailure" };
        var definition = new NodeDefinition
        {
            Id = "n1",
            Name = "n1",
            TypeName = node.TypeName,
            RetryPolicy = new RetryPolicy
            {
                MaxRetries = 3,
                BaseDelay = TimeSpan.Zero,
                // 可重试列表不含实际错误码 → 不重试。
                RetryableErrorCodes = ["OtherCode"]
            }
        };
        var context = new NodeExecutionContext { Node = definition, ExecutionId = Guid.NewGuid() };

        var result = await _executor.ExecuteNodeWithRetryAsync(definition, node, context, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("RetryFailure", result.Error!.Code);
        // 未重试：仅调用一次。
        Assert.Equal(1, node.Calls);
    }

    [Fact]
    public async Task ExecuteNodeWithRetryAsync_NodeTimeout_ReturnsTimeoutError()
    {
        var node = new DelayingNode { DelayMs = 500 };
        var definition = new NodeDefinition
        {
            Id = "n1",
            Name = "n1",
            TypeName = node.TypeName,
            // 节点自身超时 50ms，远小于节点延迟，触发超时分支。
            Timeout = TimeSpan.FromMilliseconds(50)
        };
        var context = new NodeExecutionContext { Node = definition, ExecutionId = Guid.NewGuid() };

        var result = await _executor.ExecuteNodeWithRetryAsync(definition, node, context, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("Timeout", result.Error!.Code);
        Assert.True(node.Calls >= 1);
    }

    [Fact]
    public async Task ExecuteNodeWithRetryAsync_FixedBackoff_AppliesDelayBetweenRetries()
    {
        var node = new FlakyNode { FailCount = 1, ErrorCode = "RetryFailure" };
        var definition = new NodeDefinition
        {
            Id = "n1",
            Name = "n1",
            TypeName = node.TypeName,
            RetryPolicy = new RetryPolicy
            {
                MaxRetries = 3,
                BaseDelay = TimeSpan.FromMilliseconds(100),
                BackoffStrategy = BackoffStrategy.Fixed,
                RetryableErrorCodes = ["RetryFailure"]
            }
        };
        var context = new NodeExecutionContext { Node = definition, ExecutionId = Guid.NewGuid() };

        var sw = Stopwatch.StartNew();
        var result = await _executor.ExecuteNodeWithRetryAsync(definition, node, context, CancellationToken.None);
        sw.Stop();

        Assert.True(result.Success);
        // Fixed 策略：1 次重试约 100ms 延迟，留余量断言已应用退避。
        Assert.InRange(sw.ElapsedMilliseconds, 80, 2000);
    }

    [Theory]
    [InlineData(BackoffStrategy.Exponential, 0, 100, 100)]
    [InlineData(BackoffStrategy.Exponential, 1, 100, 200)]
    [InlineData(BackoffStrategy.Exponential, 2, 100, 400)]
    [InlineData(BackoffStrategy.Linear, 0, 100, 100)]
    [InlineData(BackoffStrategy.Linear, 1, 100, 200)]
    [InlineData(BackoffStrategy.Linear, 2, 100, 300)]
    [InlineData(BackoffStrategy.Fixed, 0, 100, 100)]
    [InlineData(BackoffStrategy.Fixed, 1, 100, 100)]
    [InlineData(BackoffStrategy.Fixed, 2, 100, 100)]
    public void CalculateBackoff_StrategyAndAttempt_ComputesExpectedDelay(
        BackoffStrategy strategy, int attempt, int baseDelayMs, int expectedMs)
    {
        var policy = new RetryPolicy
        {
            BaseDelay = TimeSpan.FromMilliseconds(baseDelayMs),
            MaxDelay = TimeSpan.FromSeconds(60),
            BackoffStrategy = strategy
        };

        var delay = RetryExecutor.CalculateBackoff(policy, attempt);

        Assert.Equal(TimeSpan.FromMilliseconds(expectedMs), delay);
    }

    [Fact]
    public void CalculateBackoff_ExceedsMaxDelay_CapsAtMaxDelay()
    {
        var policy = new RetryPolicy
        {
            BaseDelay = TimeSpan.FromMilliseconds(1000),
            MaxDelay = TimeSpan.FromMilliseconds(1500),
            BackoffStrategy = BackoffStrategy.Exponential
        };

        // attempt=2 → base*4 = 4000ms，超过 MaxDelay 1500ms，应被截断。
        var delay = RetryExecutor.CalculateBackoff(policy, 2);

        Assert.Equal(TimeSpan.FromMilliseconds(1500), delay);
    }

    /// <summary>
    /// 前 <see cref="FailCount"/> 次执行失败，之后成功的可配置测试节点。
    /// </summary>
    private sealed class FlakyNode : INodeType
    {
        private int _calls;

        public int Calls => _calls;

        public int FailCount { get; set; } = 1;

        public string ErrorCode { get; set; } = "RetryFailure";

        public string TypeName => "flaky";

        public string DisplayName => "Flaky";

        public string Category => "Test";

        public string Icon => "test";

        public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;

        public IReadOnlyList<PortDefinition> Ports { get; } =
        [
            new PortDefinition { Name = FlowConstants.PortNames.Input, Direction = PortDirection.Input, Type = PortType.Main },
            new PortDefinition { Name = FlowConstants.PortNames.Output, Direction = PortDirection.Output, Type = PortType.Main }
        ];

        public bool DefaultIsEntry => false;

        public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default)
        {
            var call = Interlocked.Increment(ref _calls);
            if (call <= FailCount)
            {
                return Task.FromResult(new NodeExecutionResult
                {
                    Success = false,
                    Error = new NodeError
                    {
                        Code = ErrorCode,
                        Message = "重试中失败。",
                        NodeDefinitionId = context.Node.Id
                    }
                });
            }

            return Task.FromResult(new NodeExecutionResult
            {
                Success = true,
                Output = new DataBatch
                {
                    Items =
                    [
                        new DataItem { Data = "ok", Success = true, SourceIndex = 0 }
                    ]
                }
            });
        }
    }

    /// <summary>
    /// 固定延迟的测试节点，用于触发节点超时分支。
    /// </summary>
    private sealed class DelayingNode : INodeType
    {
        public int DelayMs { get; set; } = 500;

        public string TypeName => "delaying";

        public string DisplayName => "Delaying";

        public string Category => "Test";

        public string Icon => "test";

        public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;

        public IReadOnlyList<PortDefinition> Ports { get; } =
        [
            new PortDefinition { Name = FlowConstants.PortNames.Input, Direction = PortDirection.Input, Type = PortType.Main },
            new PortDefinition { Name = FlowConstants.PortNames.Output, Direction = PortDirection.Output, Type = PortType.Main }
        ];

        public bool DefaultIsEntry => false;

        public int Calls { get; private set; }

        public async Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default)
        {
            Calls++;
            await Task.Delay(DelayMs, cancellationToken).ConfigureAwait(false);
            return new NodeExecutionResult { Success = true, Output = new DataBatch() };
        }
    }
}

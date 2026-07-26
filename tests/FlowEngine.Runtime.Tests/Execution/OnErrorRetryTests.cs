using System;
using System.Threading;
using System.Threading.Tasks;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Attributes;
using FlowEngine.Core.Configuration;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Exceptions;
using FlowEngine.Plugins.Standard;
using FlowEngine.Runtime.Executor;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FlowEngine.Runtime.Tests.Execution;

/// <summary>
/// 验证计划 §B.6 的 OnError×Retry 顺序：
/// 1) 可重试（Transient）错误由 RetryExecutor 重试，耗尽后由 OnErrorAsync 降级为成功输出；
/// 2) 不可重试（Fatal）错误不重试，OnErrorAsync 立即降级为成功输出。
/// 基类适配层捕获 NodeExecutionException 并调用 OnErrorAsync，降级输出作为成功结果（Success=true）回流。
/// </summary>
public sealed class OnErrorRetryTests
{
    [NodeMeta(TypeName = "testRetry", DisplayName = "TestRetry", Category = NodeCategory.Test, Icon = "test")]
    [Port(FlowConstants.PortNames.Input, "Input", PortDirection.Input)]
    [Port(FlowConstants.PortNames.Output, "Output", PortDirection.Output)]
    private sealed class TransientThenDegradeNode : NodeBase
    {
        private readonly int _degradeAfter;
        public int HandlerCalls { get; private set; }
        public int OnErrorCalls { get; private set; }

        public TransientThenDegradeNode(int degradeAfter) => _degradeAfter = degradeAfter;

        public override Task<NodeHandlerOutput> ExecuteAsync(NodeInput input, CancellationToken ct)
        {
            HandlerCalls++;
            throw new NodeExecutionException("Transient", "x");
        }

        protected override Task<NodeHandlerOutput?> OnErrorAsync(NodeErrorContext ctx, CancellationToken ct)
        {
            OnErrorCalls++;
            // 前 _degradeAfter 次不降级（返回 null），让 RetryExecutor 继续重试；
            // 超过后返回降级输出，模拟“重试耗尽 → OnErrorAsync 降级”。
            if (OnErrorCalls <= _degradeAfter)
            {
                return Task.FromResult<NodeHandlerOutput?>(null);
            }

            return Task.FromResult<NodeHandlerOutput?>(NodeHandlerOutput.Data(new DataBatch()));
        }
    }

    [NodeMeta(TypeName = "testFatal", DisplayName = "TestFatal", Category = NodeCategory.Test, Icon = "test")]
    [Port(FlowConstants.PortNames.Input, "Input", PortDirection.Input)]
    [Port(FlowConstants.PortNames.Output, "Output", PortDirection.Output)]
    private sealed class FatalNode : NodeBase
    {
        public int HandlerCalls { get; private set; }
        public int OnErrorCalls { get; private set; }

        public override Task<NodeHandlerOutput> ExecuteAsync(NodeInput input, CancellationToken ct)
        {
            HandlerCalls++;
            throw new NodeExecutionException("Fatal", "y");
        }

        protected override Task<NodeHandlerOutput?> OnErrorAsync(NodeErrorContext ctx, CancellationToken ct)
        {
            OnErrorCalls++;
            return Task.FromResult<NodeHandlerOutput?>(NodeHandlerOutput.Data(new DataBatch()));
        }
    }

    private static RetryExecutor CreateExecutor() =>
        new(
            new EngineDefaultsOptions { DefaultBaseDelaySeconds = 0, DefaultMaxDelaySeconds = 0 },
            new ErrorStrategyHandler(),
            NullLogger<RetryExecutor>.Instance);

    [Fact]
    public async Task TransientError_IsRetriedThenDegraded()
    {
        const int n = 3;
        var node = new TransientThenDegradeNode(degradeAfter: n);
        var retry = CreateExecutor();
        var nodeDef = new NodeDefinition
        {
            Id = "n1",
            TypeName = "testRetry",
            Name = "n1",
            RetryPolicy = new RetryPolicy
            {
                MaxRetries = n,
                RetryableErrorCodes = new List<string> { "Transient" },
                BaseDelay = TimeSpan.Zero,
                MaxDelay = TimeSpan.Zero,
            }
        };

        var result = await retry.ExecuteNodeWithRetryAsync(nodeDef, node, new NodeExecutionContext(), CancellationToken.None);

        // 重试 N 次后由 OnErrorAsync 降级为成功输出。
        Assert.True(result.Success, result.Error?.Message);
        Assert.Empty(result.Output.Items); // 降级输出为空批次
        Assert.Equal(n + 1, node.HandlerCalls); // 1 次初始 + N 次重试
        Assert.Equal(n + 1, node.OnErrorCalls);
    }

    [Fact]
    public async Task NonRetryableError_IsNotRetriedAndDegradedImmediately()
    {
        var node = new FatalNode();
        var retry = CreateExecutor();
        var nodeDef = new NodeDefinition
        {
            Id = "n2",
            TypeName = "testFatal",
            Name = "n2",
            RetryPolicy = new RetryPolicy
            {
                MaxRetries = 3, // 即便配置了重试，错误码不在可重试列表也不重试
                RetryableErrorCodes = new List<string> { "Transient" },
                BaseDelay = TimeSpan.Zero,
                MaxDelay = TimeSpan.Zero,
            }
        };

        var result = await retry.ExecuteNodeWithRetryAsync(nodeDef, node, new NodeExecutionContext(), CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Empty(result.Output.Items);
        Assert.Equal(1, node.HandlerCalls); // 不重试
        Assert.Equal(1, node.OnErrorCalls);
    }
}

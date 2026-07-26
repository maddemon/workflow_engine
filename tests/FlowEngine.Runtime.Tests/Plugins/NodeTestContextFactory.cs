using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Scripting;
using FlowEngine.Runtime.Expressions;
using FlowEngine.Runtime.Executor;
using FlowEngine.Runtime.Registry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FlowEngine.Runtime.Tests.Plugins;

/// <summary>
/// 为 Plugins.Standard 节点测试提供统一上下文构造工厂。
/// </summary>
internal static class NodeTestContextFactory
{
    public static async Task<NodeExecutionContext> BuildAsync(
        INodeType nodeInstance,
        Dictionary<string, object>? parameters = null,
        Dictionary<string, DataBatch>? inputs = null,
        IDictionary<string, JsonNode?>? memory = null)
    {
        parameters ??= new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        inputs ??= new Dictionary<string, DataBatch>(StringComparer.OrdinalIgnoreCase);

        var registry = new NodeRegistry(new List<INodeType> { nodeInstance }, NullLogger<NodeRegistry>.Instance);
        var scriptCache = new ScriptCache(Options.Create(new JsEngineOptions()));
        var factory = new NodeExecutionContextFactory(
            registry,
            scriptCache,
            new ParameterResolver(
                NullLogger<ParameterResolver>.Instance,
                Options.Create(new JsEngineOptions()),
                scriptCache),
            new NullCredentialAccessor(),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        var nodeDef = new NodeDefinition
        {
            Id = nodeInstance.TypeName + "1",
            TypeName = nodeInstance.TypeName,
            Name = nodeInstance.TypeName,
            Parameters = parameters
        };

        var context = await factory.CreateAsync(
            new Workflow { Id = Guid.NewGuid(), Name = "test" },
            new ExecutionRecord { Id = Guid.NewGuid() },
            nodeDef,
            nodeInstance,
            inputs,
            new Dictionary<string, DataBatch>(),
            new Dictionary<string, DataBatch>(),
            0,
            CancellationToken.None).ConfigureAwait(false);

        if (memory is not null)
        {
            context.Memory = memory;
        }

        // 模拟生产管线 ExecutionStage 的能力注入：将上下文派生能力（Ctx / Logger / NodeContext /
        // ILlmClient / Engine）注入节点，使直接执行节点的单测与经管线执行行为一致。
        // Registry 经 DI 解析，故构建包含 INodeRegistry 的 ServiceProvider（其余 DI 能力暂未注册，解析为 null）。
        if (nodeInstance is NodeBase nb)
        {
            var sp = new ServiceCollection()
                .AddSingleton<INodeRegistry>(registry)
                .AddSingleton<INodeExecutionContextFactory>(factory)
                .BuildServiceProvider();
            NodeCapabilityInjector.Inject(nb, sp, context);
        }

        return context;
    }

    private sealed class NullCredentialAccessor : ICredentialAccessor
    {
        public Task<CredentialValue> GetCredentialAsync(Guid credentialId, CancellationToken cancellationToken = default)
            => Task.FromResult<CredentialValue>(null!);

        public Task<CredentialValue?> GetCredentialByNameAsync(string name, CancellationToken cancellationToken = default)
            => Task.FromResult<CredentialValue?>(null);
    }
}

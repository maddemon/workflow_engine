using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Exceptions;
using FlowEngine.Core.Scripting;
using Microsoft.Extensions.DependencyInjection;

namespace FlowEngine.Core.Abstractions;

/// <summary>
/// 节点能力注入器：将 DI 服务或运行上下文值按节点声明的 <see cref="InjectAttribute"/> 注入到
/// <see cref="NodeBase"/> 派生节点。节点只声明需要的能力（如 <see cref="ILlmClient"/> / <see cref="IHttpExecutionService"/>），
/// 不关心其来自 DI 容器还是运行上下文，从而剥离 <see cref="NodeBase"/> 上的"能力属性"与 god-object 耦合。
/// </summary>
public static class NodeCapabilityInjector
{
    /// <summary>按节点类型缓存的 [Inject] 属性扫描结果，避免每次执行重复反射。</summary>
    private static readonly ConcurrentDictionary<Type, (PropertyInfo Prop, InjectAttribute Attr)[]> _injectProps = new();

    /// <summary>
    /// 运行期上下文值提供器：命中此字典的能力直接取自 <see cref="NodeExecutionContext"/>（"每运行/每节点"上下文值，不走 DI）。
    /// </summary>
    private static readonly Dictionary<Type, Func<NodeExecutionContext, object?>> ContextProviders = new()
    {
        [typeof(JsEngine)] = ctx => ctx.GetOrCreateEngine(),
        [typeof(NodeContext)] = ctx => ctx.NodeContext is null ? new NodeContext() : new NodeContext(ctx.NodeContext),
        [typeof(NodeExecutionContext)] = ctx => ctx,
        [typeof(ILlmClient)] = ctx => ctx.LlmClient,
        [typeof(IExecutionLogger)] = ctx => ctx.Logger,
        [typeof(ICredentialAccessor)] = ctx => ctx.Credentials,
        // INodeRegistry 经 ctx.NodeRegistry 解析：NodeExecutionContextFactory.CreateAsync 在构造上下文时
        // 写入 NodeRegistry = registry，故生产/测试（手动置 NodeRegistry）均可用。改为上下文能力后，
        // NodeBase 的直接执行路径不再需要为注入 INodeRegistry 而每次执行 new ServiceCollection + BuildServiceProvider。
        [typeof(INodeRegistry)] = ctx => ctx.NodeRegistry,
        // INodeExecutionContextFactory 经 ctx.ContextFactory 解析：NodeExecutionContextFactory.CreateAsync
        // 在构造上下文时写入 ContextFactory = this，故生产/测试（手动置 ContextFactory）均可用。
        [typeof(INodeExecutionContextFactory)] = ctx => ctx.ContextFactory,
    };

    /// <summary>
    /// 将能力与上下文注入到节点实例。
    /// </summary>
    /// <param name="node">待注入的节点实例（须为 <see cref="NodeBase"/> 派生类型）。</param>
    /// <param name="sp">DI 服务容器（解析 DI 注册的能力；可为 null，仅当节点不含 DI 来源能力时）。</param>
    /// <param name="ctx">节点执行上下文（提供"每运行/每节点"上下文能力）。</param>
    public static void Inject(NodeBase node, IServiceProvider? sp, NodeExecutionContext ctx)
    {
        var props = _injectProps.GetOrAdd(node.GetType(), Scan);
        foreach (var (prop, attr) in props)
        {
            var value = Resolve(prop.PropertyType, attr, sp, ctx);
            if (value is null)
            {
                if (attr.Required)
                {
                    throw new NodeExecutionException(
                        "CapabilityMissing",
                        $"节点 {node.GetType().Name} 的能力 {prop.PropertyType.Name} 未注入（Required）。");
                }

                continue;
            }

            prop.SetValue(node, value);
        }
    }

    /// <summary>按类型解析能力：命中上下文提供器则取上下文值，否则走 DI（支持 GetKeyedService 按名称解析）。</summary>
    private static object? Resolve(Type type, InjectAttribute attr, IServiceProvider? sp, NodeExecutionContext ctx)
    {
        if (ContextProviders.TryGetValue(type, out var provider))
        {
            return provider(ctx);
        }

        // DI 来源能力：无服务提供者（如部分单测未注入）时按"未解析"处理（Required 抛错 / 可选留空）。
        if (sp is null)
        {
            return null;
        }

        return attr.Name is not null
            ? sp.GetKeyedService(type, attr.Name)
            : sp.GetService(type);
    }

    /// <summary>反射收集类型上所有带 <see cref="InjectAttribute"/> 且可写公共实例属性。</summary>
    /// <param name="t">节点类型。</param>
    /// <returns>属性与特性对的数组。</returns>
    private static (PropertyInfo Prop, InjectAttribute Attr)[] Scan(Type t)
    {
        var list = new List<(PropertyInfo, InjectAttribute)>();
        foreach (var prop in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!prop.CanWrite)
            {
                continue;
            }

            var attr = prop.GetCustomAttribute<InjectAttribute>();
            if (attr is not null)
            {
                list.Add((prop, attr));
            }
        }

        return list.ToArray();
    }
}

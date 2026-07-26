using System.Collections.Concurrent;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using Microsoft.Extensions.Logging;

namespace FlowEngine.Runtime.Registry;

/// <summary>
/// 节点注册中心实现，负责缓存节点类型元数据并按类型名创建实例。
/// </summary>
public sealed class NodeRegistry : INodeRegistry
{
    private readonly ConcurrentDictionary<string, Type> _nodeTypes = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, INodeType> _instances = new(StringComparer.OrdinalIgnoreCase);
    // 延迟创建描述符：注册时不立即做参数反射，首次访问时才通过 Lazy 计算，降低启动开销（L5/L6）。
    private readonly ConcurrentDictionary<string, Lazy<NodeTypeDescriptor>> _descriptors = new(StringComparer.OrdinalIgnoreCase);
    private readonly ParameterDiscoverer _parameterDiscoverer;
    private readonly ILogger<NodeRegistry> _logger;

    /// <summary>
    /// 初始化节点注册中心。
    /// </summary>
    /// <param name="initialNodes">初始节点类型实例集合。</param>
    /// <param name="logger">日志记录器。</param>
    public NodeRegistry(IEnumerable<INodeType> initialNodes, ILogger<NodeRegistry> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _parameterDiscoverer = new ParameterDiscoverer(logger);

        foreach (var node in initialNodes)
        {
            Register(node);
        }
    }

    /// <inheritdoc />
    public void Register(INodeType nodeType)
    {
        ArgumentNullException.ThrowIfNull(nodeType);

        var normalizedName = nodeType.TypeName.ToLowerInvariant();
        if (!_nodeTypes.TryAdd(normalizedName, nodeType.GetType()))
        {
            _logger.LogWarning(
                "节点类型 {TypeName} 已存在，保留首个注册。",
                nodeType.TypeName);
            return;
        }

        // 缓存无状态节点实例，避免每次获取都反射创建（L5）。
        _instances[normalizedName] = nodeType;
        // 描述符延迟创建：首次访问 GetDescriptor(s) 时才触发参数反射。
        _descriptors[normalizedName] = new Lazy<NodeTypeDescriptor>(() => CreateDescriptor(nodeType));
        _logger.LogDebug("已注册节点类型 {TypeName}", nodeType.TypeName);
    }

    /// <summary>
    /// 按类型名获取节点类型实例（每调用返回<b>新克隆实例</b>）。
    /// 仅用于读取类型级元数据（端口 / 描述符），<b>禁止用于执行</b>：
    /// 执行必须走 <see cref="CreateInstance"/> + 管线注入，否则跳过服务注入导致能力缺失。
    /// 实现见基类 <see cref="TryGet"/>（Activator.CreateInstance 返回隔离工作实例）。
    /// </summary>
    /// <param name="typeName">节点类型名。</param>
    /// <returns>节点类型实例（全新克隆）。</returns>
    /// <exception cref="InvalidOperationException">节点类型未注册时抛出。</exception>
    public INodeType Get(string typeName)
    {
        if (!TryGet(typeName, out var nodeType) || nodeType is null)
        {
            throw new InvalidOperationException($"节点类型 '{typeName}' 未注册。");
        }

        return nodeType;
    }

    /// <inheritdoc />
    public bool TryGet(string typeName, out INodeType? nodeType)
    {
        ArgumentException.ThrowIfNullOrEmpty(typeName);

        var normalizedName = typeName.ToLowerInvariant();
        // CON-3：返回按类型克隆的全新实例（而非共享单例）。执行期 ParameterHydrator 会向该实例
        // 水合当前节点的参数（如 SwitchNode.Cases/Ports），若返回共享单例，并行执行的不同节点/
        // 执行会互相串改共享字段，导致路由错乱。克隆保证每次取用都是隔离的工作实例。
        if (_nodeTypes.TryGetValue(normalizedName, out var type))
        {
            nodeType = (INodeType?)Activator.CreateInstance(type);
            return nodeType is not null;
        }

        nodeType = null;
        return false;
    }

    /// <summary>
    /// 获取所有已注册的节点类型实例（注册期缓存的<b>共享单例</b>，非克隆）。
    /// <b>绝不可用于执行</b>：在其结果上调用 <c>ExecuteAsync</c> 是真正的共享单例数据竞争，
    /// 已被 <c>NodeApiComplianceAnalyzer</c> 的 FE0002 规则静态拦截。仅用于框架层枚举 / 目录。
    /// </summary>
    /// <returns>共享单例节点类型实例集合。</returns>
    public IReadOnlyCollection<INodeType> GetAll()
    {
        return _instances.Values.ToList();
    }

    /// <inheritdoc />
    public INodeType CreateInstance(string typeName)
    {
        var normalizedName = typeName.ToLowerInvariant();
        if (_nodeTypes.TryGetValue(normalizedName, out var type))
        {
            return (INodeType)Activator.CreateInstance(type)!;
        }

        throw new InvalidOperationException($"节点类型 '{typeName}' 未注册。");
    }

    /// <inheritdoc />
    public IReadOnlyCollection<NodeTypeDescriptor> GetDescriptors()
    {
        return _descriptors.Values.Select(lazy => lazy.Value).ToList();
    }

    /// <inheritdoc />
    public NodeTypeDescriptor GetDescriptor(string typeName)
    {
        ArgumentException.ThrowIfNullOrEmpty(typeName);

        var normalizedName = typeName.ToLowerInvariant();
        if (_descriptors.TryGetValue(normalizedName, out var lazy))
        {
            return lazy.Value;
        }

        throw new InvalidOperationException($"节点类型 '{typeName}' 未注册。");
    }

    private NodeTypeDescriptor CreateDescriptor(INodeType nodeType)
    {
        var parameters = _parameterDiscoverer.Discover(nodeType.GetType());

        return new NodeTypeDescriptor
        {
            TypeName = nodeType.TypeName,
            DisplayName = nodeType.DisplayName,
            Category = nodeType.Category,
            Icon = nodeType.Icon,
            ExecutionMode = nodeType.ExecutionMode,
            Parameters = parameters,
            Ports = nodeType.Ports,
            DefaultIsEntry = nodeType.DefaultIsEntry
        };
    }
}

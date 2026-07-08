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
    private readonly ConcurrentDictionary<string, NodeTypeDescriptor> _descriptors = new(StringComparer.OrdinalIgnoreCase);
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
        _descriptors[normalizedName] = CreateDescriptor(nodeType);
        _logger.LogDebug("已注册节点类型 {TypeName}", nodeType.TypeName);
    }

    /// <inheritdoc />
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
        if (_instances.TryGetValue(normalizedName, out var cached))
        {
            nodeType = cached;
            return true;
        }

        // 兜底：按类型创建并缓存（极少数未预注册实例的场景）。
        if (_nodeTypes.TryGetValue(normalizedName, out var type))
        {
            nodeType = (INodeType?)Activator.CreateInstance(type);
            if (nodeType is not null) _instances[normalizedName] = nodeType;
            return nodeType is not null;
        }

        nodeType = null;
        return false;
    }

    /// <inheritdoc />
    public IReadOnlyCollection<INodeType> GetAll()
    {
        return _instances.Values.ToList();
    }

    /// <inheritdoc />
    public INodeType CreateInstance(string typeName) => Get(typeName);

    /// <inheritdoc />
    public IReadOnlyCollection<NodeTypeDescriptor> GetDescriptors()
    {
        return _descriptors.Values.ToList();
    }

    /// <inheritdoc />
    public NodeTypeDescriptor GetDescriptor(string typeName)
    {
        ArgumentException.ThrowIfNullOrEmpty(typeName);

        var normalizedName = typeName.ToLowerInvariant();
        if (_descriptors.TryGetValue(normalizedName, out var descriptor))
        {
            return descriptor;
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

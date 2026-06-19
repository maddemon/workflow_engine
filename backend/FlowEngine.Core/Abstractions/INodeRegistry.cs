using FlowEngine.Core.Entities;

namespace FlowEngine.Core.Abstractions;

/// <summary>
/// 节点注册中心。
/// </summary>
public interface INodeRegistry
{
    /// <summary>
    /// 注册一个节点类型。
    /// </summary>
    /// <param name="nodeType">节点类型实例。</param>
    void Register(INodeType nodeType);

    /// <summary>
    /// 按类型名获取节点类型实例。
    /// </summary>
    /// <param name="typeName">节点类型名。</param>
    /// <returns>节点类型实例。</returns>
    /// <exception cref="InvalidOperationException">节点类型未注册时抛出。</exception>
    INodeType Get(string typeName);

    /// <summary>
    /// 尝试按类型名获取节点类型实例。
    /// </summary>
    /// <param name="typeName">节点类型名。</param>
    /// <param name="nodeType">获取到的节点类型实例。</param>
    /// <returns>是否获取成功。</returns>
    bool TryGet(string typeName, out INodeType? nodeType);

    /// <summary>
    /// 获取所有已注册的节点类型实例。
    /// </summary>
    /// <returns>节点类型实例集合。</returns>
    IReadOnlyCollection<INodeType> GetAll();

    /// <summary>
    /// 按类型名创建新的节点类型实例。
    /// </summary>
    /// <param name="typeName">节点类型名。</param>
    /// <returns>新的节点类型实例。</returns>
    INodeType CreateInstance(string typeName);

    /// <summary>
    /// 获取所有已注册节点类型的元数据描述。
    /// </summary>
    /// <returns>节点类型描述集合。</returns>
    IReadOnlyCollection<NodeTypeDescriptor> GetDescriptors();

    /// <summary>
    /// 按类型名获取节点类型的元数据描述。
    /// </summary>
    /// <param name="typeName">节点类型名。</param>
    /// <returns>节点类型描述。</returns>
    /// <exception cref="InvalidOperationException">节点类型未注册时抛出。</exception>
    NodeTypeDescriptor GetDescriptor(string typeName);
}

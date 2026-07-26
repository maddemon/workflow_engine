namespace FlowEngine.Core.Abstractions;
/// <summary>节点业务处理接口。替代 INodeType 的 ExecuteAsync 重载：节点只关心拿输入→产生输出；
/// 业务失败用 throw <see cref="NodeExecutionException"/> 表达，由框架统一转换为 <see cref="NodeExecutionResult"/>。</summary>
public interface INodeHandler
{
    /// <summary>执行节点业务逻辑。不负责参数校验、异常转换、路由等横切关注点。</summary>
    /// <param name="input">节点输入视图。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>节点业务输出（纯数据）。</returns>
    Task<NodeHandlerOutput> ExecuteAsync(NodeInput input, CancellationToken ct);
}

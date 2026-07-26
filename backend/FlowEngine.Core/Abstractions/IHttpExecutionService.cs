using System.Threading;
using System.Threading.Tasks;
using FlowEngine.Core;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Http;

namespace FlowEngine.Core.Abstractions;

/// <summary>
/// HTTP 执行服务抽象：节点经此接口发起 HTTP 请求，取代经 <see cref="NodeExecutionContext"/> 直接依赖
/// 具体 <see cref="HttpExecutionService"/> 的方式，便于 Phase 4 节点迁移与单元测试替换。
/// </summary>
public interface IHttpExecutionService
{
    /// <summary>执行 HTTP 请求，语义与 <see cref="HttpExecutionService.ExecuteAsync"/> 一致。</summary>
    /// <param name="request">HTTP 请求描述。</param>
    /// <param name="context">节点执行上下文（提供客户端池、凭据解析等）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>节点执行结果。</returns>
    Task<NodeExecutionResult> ExecuteAsync(HttpExecutionRequest request, NodeExecutionContext context, CancellationToken ct = default);
}

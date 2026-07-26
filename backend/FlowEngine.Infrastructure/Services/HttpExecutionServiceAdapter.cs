using System.Threading;
using System.Threading.Tasks;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Http;

namespace FlowEngine.Infrastructure.Services;

/// <summary>
/// HTTP 执行服务适配器：委托给 <see cref="HttpExecutionService"/> 实现，
/// 节点经 <see cref="IHttpExecutionService"/> 接口使用，避免直接依赖具体类型。
/// </summary>
public sealed class HttpExecutionServiceAdapter : IHttpExecutionService
{
    private readonly HttpExecutionService _inner = new();

    /// <inheritdoc />
    public Task<NodeExecutionResult> ExecuteAsync(HttpExecutionRequest request, NodeExecutionContext context, CancellationToken ct = default)
        => _inner.ExecuteAsync(request, context, ct);
}

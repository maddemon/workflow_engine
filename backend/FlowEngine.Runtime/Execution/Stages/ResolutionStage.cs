using FlowEngine.Core.Abstractions;
using FlowEngine.Runtime.Execution.Pipeline;

namespace FlowEngine.Runtime.Execution.Stages;

/// <summary>
/// 参数求值阶段。Phase 2 占位：<see cref="NodeExecutionContextFactory.CreateAsync"/> 已完成
/// Script 预求值，LLM 客户端解析保留在执行阶段内（保持行为一致）。本阶段在 Phase 4 承载
/// NodeBase 节点的 DI 服务注入：将 HTTP/子执行/工具解析服务绑定到 <see cref="NodeBase"/> 派生节点，
/// 使其无需经构造函数即可获取基础设施服务。非 NodeBase 节点（仍实现 INodeType）不受影响。
/// </summary>
public sealed class ResolutionStage : IExecutionStage
{
    private readonly IHttpExecutionService? _httpService;
    private readonly ISubExecutionService? _subService;

    /// <summary>构造解析阶段。</summary>
    /// <param name="httpService">HTTP 执行服务（可选，由 DI 注入；为 null 时仅 HttpRequestNode 类节点不可用）。</param>
    /// <param name="subService">子执行服务（可选，由 DI 注入；为 null 时仅 AgentNode 类节点不可用）。</param>
    public ResolutionStage(IHttpExecutionService? httpService = null, ISubExecutionService? subService = null)
    {
        _httpService = httpService;
        _subService = subService;
    }

    /// <summary>运行解析阶段：为 NodeBase 派生节点注入 DI 服务，随后驱动后续阶段。</summary>
    /// <param name="context">管线上下文。</param>
    /// <param name="next">后续阶段驱动委托。</param>
    /// <param name="ct">取消令牌。</param>
    public Task RunAsync(NodePipelineContext context, Func<Task> next, CancellationToken ct)
    {
        // 仅为 NodeBase 派生节点注入服务；ISubExecutionService 当前为 scoped 服务，由内核以 null 传入（AgentNode 尚在遗留路径）。
        if (context.NodeType is NodeBase nb)
        {
            nb.BindServices(_httpService, _subService, null);
        }

        return next();
    }
}

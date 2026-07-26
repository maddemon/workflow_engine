using System.Collections.Generic;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Runtime.Executor;

namespace FlowEngine.Runtime.Execution.Pipeline;

/// <summary>节点执行管线在阶段间共享的上下文对象。
/// 各阶段（InitializeStage / ResolutionStage / ValidationStage / ExecutionStage / PersistenceStage 等）按职责填充对应属性，
/// 当前阶段仅定义可设置/可读取的载体，具体填充逻辑在后续阶段实现中完成。
///
/// 本对象按设计保留为较大的共享对象（当前共 19 个属性），并非节点侧"精简视图"。其接口隔离（IValidationContext / IExecutionContext 等）
/// 列为计划 plan-node-execution-pipeline §5 的暂缓项：按 §5.2 的触发条件，当属性数达到/超过 NodeExecutionContext 当前 28 个，
/// 或新增第 5 个以上 IExecutionStage 需向其中写入专属属性时，才重新评估并启动接口隔离。现阶段未达触发条件，故保持现状、不做拆分。
/// 真正"精简"的视图是节点侧 <see cref="FlowEngine.Core.Abstractions.NodeInput"/>；节点不得经本对象索取数据/服务或自构错误（见计划 §A.7）。</summary>
public sealed class NodePipelineContext
{
    /// <summary>本次待执行的工作项（含输入数据批次、节点实例 ID 等）。由构造器注入，后续阶段据此读取输入。</summary>
    public NodeWorkItem Item { get; }

    /// <summary>节点定义（DTO/实体），由初始化阶段填充。</summary>
    public NodeDefinition? NodeDefinition { get; set; }

    /// <summary>节点类型契约（解析自注册中心），由初始化阶段填充。</summary>
    public INodeType? NodeType { get; set; }

    /// <summary>声明式校验错误集合（[Required]/类型约束等），由校验阶段填充。</summary>
    public List<ValidationError> ValidationErrors { get; set; } = new();

    /// <summary>求值后的参数映射（表达式已解析为具体值），由参数求值时阶段填充。</summary>
    public IReadOnlyDictionary<string, object>? ResolvedParameters { get; set; }

    /// <summary>凭据访问器，用于在执行阶段获取节点所需凭据。</summary>
    public ICredentialAccessor? Credentials { get; set; }

    /// <summary>全局变量（工作流级），供表达式求值与节点执行引用。</summary>
    public IReadOnlyDictionary<string, object?>? GlobalVariables { get; set; }

    /// <summary>LLM 客户端，供 AI/LLM 类节点执行使用。</summary>
    public ILlmClient? LlmClient { get; set; }

    /// <summary>节点处理器（INodeHandler）的输出，由执行阶段填充。</summary>
    public NodeHandlerOutput? HandlerOutput { get; set; }

    /// <summary>节点执行结果；任一阶段设置后，驱动器据其决定是否短路直达末端持久化阶段。</summary>
    public NodeExecutionResult? Result { get; set; }

    /// <summary>执行会话（封装本次工作流执行的可变状态）。由构造器注入，只读。</summary>
    public ExecutionSession Session { get; }

    /// <summary>执行副作用回调（持久化/事件发布等）。由构造器注入，只读。</summary>
    public IExecutionSideEffects SideEffects { get; }

    /// <summary>原始参数快照（未求值的入参），用于审计或回放。</summary>
    public IReadOnlyDictionary<string, object>? RawParametersSnapshot { get; set; }

    /// <summary>节点级持久化上下文（跨多次运行保持状态，如 LoopNode 迭代位置），由初始化阶段填充。</summary>
    public IDictionary<string, object?>? NodeContext { get; set; }

    /// <summary>节点执行模式（由节点类型解析），由初始化阶段填充，供执行阶段计算运行输入。</summary>
    public ExecutionMode ExecutionMode { get; set; }

    /// <summary>本次节点运行次数（OncePerItem 按最大输入项批量展开），由初始化阶段填充，供执行阶段循环。</summary>
    public int RunCount { get; set; }

    /// <summary>是否已由执行阶段完成逐次持久化。
    /// 为 true 时 <see cref="PersistenceStage"/> 跳过重复持久化（正常路径已落库）。
    /// 短路路径（校验/环路上限失败）下保持 false，由 <see cref="PersistenceStage"/> 负责落库。</summary>
    public bool ExecutedAndPersisted { get; set; }

    /// <summary>是否应终止整个工作流：环路上限或节点失败且错误策略非 Continue 时置真。
    /// 驱动器据此短路至末端阶段，且 <see cref="PersistenceStage"/> 据此跳过重复持久化。</summary>
    public bool ShouldTerminateWorkflow { get; set; }

    /// <summary>路由结果（已累积成功输出批，含 BranchIndex/Error/ToolExecutionRecords 传递），
    /// 由执行阶段构造，供路由阶段分发至下游。</summary>
    public NodeExecutionResult? RoutingResult { get; set; }

    /// <summary>构造管线上下文。</summary>
    /// <param name="item">本次待执行的工作项。</param>
    /// <param name="session">执行会话。</param>
    /// <param name="sideEffects">执行副作用回调。</param>
    public NodePipelineContext(NodeWorkItem item, ExecutionSession session, IExecutionSideEffects sideEffects)
        => (Item, Session, SideEffects) = (item, session, sideEffects);
}

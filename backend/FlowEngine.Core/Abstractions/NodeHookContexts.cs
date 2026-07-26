using FlowEngine.Core.Entities;

namespace FlowEngine.Core.Abstractions;
/// <summary>节点执行前钩子上下文，携带精简输入视图与原始执行上下文（可选）。</summary>
public sealed class NodeExecutingContext
{
    /// <summary>精简输入视图。</summary>
    public NodeInput Input { get; }

    /// <summary>原始节点执行上下文（可为 null，框架透传用于高级场景）。</summary>
    public NodeExecutionContext? RawContext { get; }

    /// <summary>构造执行前上下文。</summary>
    /// <param name="input">输入视图。</param>
    /// <param name="rawContext">原始上下文。</param>
    public NodeExecutingContext(NodeInput input, NodeExecutionContext? rawContext) => (Input, RawContext) = (input, rawContext);
}

/// <summary>节点执行后钩子上下文，携带业务输出与包装后的执行结果（可能为 null）。</summary>
public sealed class NodeExecutedContext
{
    /// <summary>业务输出。</summary>
    public NodeHandlerOutput? Output { get; }

    /// <summary>框架包装后的执行结果。</summary>
    public NodeExecutionResult? Result { get; }

    /// <summary>构造执行后上下文。</summary>
    /// <param name="output">业务输出。</param>
    /// <param name="result">执行结果。</param>
    public NodeExecutedContext(NodeHandlerOutput? output, NodeExecutionResult? result) => (Output, Result) = (output, result);
}

/// <summary>节点错误钩子上下文，携带异常与原始执行上下文（可选）。</summary>
public sealed class NodeErrorContext
{
    /// <summary>捕获的异常。</summary>
    public Exception Exception { get; }

    /// <summary>原始节点执行上下文（可为 null）。</summary>
    public NodeExecutionContext? RawContext { get; }

    /// <summary>构造错误上下文。</summary>
    /// <param name="exception">异常。</param>
    /// <param name="rawContext">原始上下文。</param>
    public NodeErrorContext(Exception exception, NodeExecutionContext? rawContext) => (Exception, RawContext) = (exception, rawContext);
}

/// <summary>节点注册钩子上下文（占位标记类型）。</summary>
public sealed class NodeRegistrationContext
{
}

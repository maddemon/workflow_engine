using System;

namespace FlowEngine.Core.Abstractions;

/// <summary>标记节点需要从 DI 容器或运行上下文注入的能力属性。节点只读该属性，不关心来源。</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class InjectAttribute : Attribute
{
    /// <summary>可选名称：属性类型不足以区分多来源时指定具体来源（走 DI GetKeyedService）。</summary>
    public string? Name { get; set; }

    /// <summary>为 true 时若解析不到即抛 <see cref="NodeExecutionException"/>；默认 false（由节点自行判空）。</summary>
    public bool Required { get; set; }
}

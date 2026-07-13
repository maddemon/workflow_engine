namespace FlowEngine.Core.Entities;

/// <summary>
/// 结构化差异记录——描述工作流修改操作的具体变更。
/// </summary>
/// <remarks>
/// 原定义在 Application/Dtos/WorkflowAssemblyDtos.cs，因 Workflow 实体（Core）需要引用此类型
/// 作为 JSON 列字段，故迁至 Core/Entities/。
/// </remarks>
public sealed record StructuredDiff
{
    /// <summary>
    /// 操作类型："modify" | "add" | "remove" | "connect" | "disconnect"
    /// </summary>
    public string Op { get; init; } = string.Empty;

    /// <summary>
    /// 受影响的节点 ID（add/remove/modify 时有效）。
    /// </summary>
    public string? NodeId { get; init; }

    /// <summary>
    /// 受影响的字段名（modify 时有效，如 "name"、"parameters.url"）。
    /// </summary>
    public string? Field { get; init; }

    /// <summary>
    /// 修改前的值（modify/remove/disconnect 时有效）。
    /// </summary>
    public object? Before { get; init; }

    /// <summary>
    /// 修改后的值（modify/add/connect 时有效）。
    /// </summary>
    public object? After { get; init; }
}

namespace FlowEngine.Application.Dtos;

/// <summary>
/// 拒绝草稿请求。
/// </summary>
public sealed record RejectDraftRequest
{
    /// <summary>
    /// 拒绝理由。
    /// </summary>
    public string Reason { get; init; } = string.Empty;
}

namespace FlowEngine.Core.Authorization;

/// <summary>
/// 资源访问裁定结果，用于区分拒绝原因（角色不足 vs 资源归属不符）。
/// </summary>
public enum AccessDecision
{
    /// <summary>允许访问。</summary>
    Allowed = 0,

    /// <summary>角色/作用域权限不足被拒。</summary>
    DeniedByRole = 1,

    /// <summary>非资源所有者被拒（Admin 除外）。</summary>
    DeniedByOwnership = 2,
}

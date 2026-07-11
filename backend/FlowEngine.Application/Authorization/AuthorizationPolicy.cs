using FlowEngine.Core.Authorization;

namespace FlowEngine.Application.Authorization;

/// <summary>
/// 声明式授权策略，显式表达每个操作的授权要求。
/// 本质差异（admin-only、project-scoped）通过命名字段区分，reviewer 一眼可见。
/// </summary>
public sealed record AuthorizationPolicy(
    ResourceKind? Resource,
    Operation? Access,
    Scope? Scope,
    bool AdminPhase,
    bool ProjectScoped);

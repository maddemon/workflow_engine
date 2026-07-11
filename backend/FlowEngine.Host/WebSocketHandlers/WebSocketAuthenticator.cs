using FlowEngine.Application.Authorization;
using FlowEngine.Application.Identity;
using FlowEngine.Core.Authorization;

namespace FlowEngine.Host.WebSocketHandlers;

/// <summary>
/// WebSocket 连接认证与授权。
/// </summary>
internal sealed class WebSocketAuthenticator
{
    private readonly IUserContext _userContext;
    private readonly IResourceAuthorizationService _resourceAuthorization;

    /// <summary>
    /// 初始化 WebSocket 认证器。
    /// </summary>
    public WebSocketAuthenticator(
        IUserContext userContext,
        IResourceAuthorizationService resourceAuthorization)
    {
        _userContext = userContext;
        _resourceAuthorization = resourceAuthorization;
    }

    /// <summary>
    /// 当前用户是否已认证。
    /// </summary>
    public bool IsAuthenticated => _userContext.IsAuthenticated;

    /// <summary>
    /// 当前用户 ID（未认证时为 null）。
    /// </summary>
    public Guid? UserId => _userContext.UserId;

    /// <summary>
    /// 尝试获取已认证用户的 ID。
    /// </summary>
    /// <returns>用户 ID；未认证或缺少用户 ID 时返回 null。</returns>
    public Guid? TryGetUserId()
    {
        if (!_userContext.IsAuthenticated || _userContext.UserId is not { } userId)
        {
            return null;
        }

        return userId;
    }

    /// <summary>
    /// 检查用户是否有权访问指定执行记录。
    /// </summary>
    public Task<bool> CanAccessExecutionAsync(
        Guid userId,
        Guid executionId,
        Operation operation,
        CancellationToken cancellationToken)
    {
        return _resourceAuthorization.CanAccessExecutionAsync(userId, executionId, operation, cancellationToken);
    }
}

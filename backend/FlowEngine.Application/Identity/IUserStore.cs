using FlowEngine.Core.Identity;

namespace FlowEngine.Application.Identity;

/// <summary>
/// 用户仓储接口，提供用户与角色的持久化操作。
/// </summary>
public interface IUserStore
{
    /// <summary>
    /// 根据 ID 获取用户。
    /// </summary>
    /// <param name="id">用户 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>用户实例，不存在时返回 null。</returns>
    Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 根据邮箱获取用户。
    /// </summary>
    /// <param name="email">邮箱地址。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>用户实例，不存在时返回 null。</returns>
    Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// 创建新用户。
    /// </summary>
    /// <param name="user">用户实体。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>创建后的用户实体（含生成的 ID）。</returns>
    Task<User> CreateAsync(User user, CancellationToken cancellationToken = default);

    /// <summary>
    /// 更新用户信息。
    /// </summary>
    /// <param name="user">用户实体。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task UpdateAsync(User user, CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除用户（软删除）。
    /// </summary>
    /// <param name="id">用户 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取用户的角色列表。
    /// </summary>
    /// <param name="userId">用户 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>角色列表。</returns>
    Task<IReadOnlyList<UserRole>> GetRolesAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 为用户添加角色。
    /// </summary>
    /// <param name="userId">用户 ID。</param>
    /// <param name="role">角色名称。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task AddRoleAsync(Guid userId, string role, CancellationToken cancellationToken = default);

    /// <summary>
    /// 移除用户角色。
    /// </summary>
    /// <param name="userId">用户 ID。</param>
    /// <param name="role">角色名称。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task RemoveRoleAsync(Guid userId, string role, CancellationToken cancellationToken = default);
}

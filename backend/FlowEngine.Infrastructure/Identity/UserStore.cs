using FlowEngine.Application.Identity;
using FlowEngine.Core.Data;
using FlowEngine.Core.Identity;
using Microsoft.EntityFrameworkCore;

namespace FlowEngine.Infrastructure.Identity;

/// <summary>
/// 用户仓储实现，基于 EF Core 访问数据库。
/// </summary>
/// <param name="dbContext">数据库上下文。</param>
public class UserStore(FlowEngineDbContext dbContext) : IUserStore
{
    /// <inheritdoc />
    public async Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == id && !u.Deleted, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        return await dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Email == email && !u.Deleted, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<User> CreateAsync(User user, CancellationToken cancellationToken = default)
    {
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync(cancellationToken);
        return user;
    }

    /// <inheritdoc />
    public async Task UpdateAsync(User user, CancellationToken cancellationToken = default)
    {
        user.UpdatedAt = DateTime.UtcNow;
        dbContext.Users.Update(user);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (user is not null)
        {
            user.Deleted = true;
            user.UpdatedAt = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserRole>> GetRolesAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await dbContext.UserRoles
            .AsNoTracking()
            .Where(ur => ur.UserId == userId && !ur.Deleted)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task AddRoleAsync(Guid userId, string role, CancellationToken cancellationToken = default)
    {
        var exists = await dbContext.UserRoles
            .AnyAsync(ur => ur.UserId == userId && ur.Role == role && !ur.Deleted, cancellationToken);

        if (!exists)
        {
            dbContext.UserRoles.Add(new UserRole { UserId = userId, Role = role });
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task RemoveRoleAsync(Guid userId, string role, CancellationToken cancellationToken = default)
    {
        var userRole = await dbContext.UserRoles
            .FirstOrDefaultAsync(ur => ur.UserId == userId && ur.Role == role && !ur.Deleted, cancellationToken);

        if (userRole is not null)
        {
            userRole.Deleted = true;
            userRole.UpdatedAt = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}

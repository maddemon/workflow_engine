using System.Linq;
using FlowEngine.Application.Identity;
using FlowEngine.Core.Abstractions;
using Microsoft.Extensions.Options;

namespace FlowEngine.Application.Security;

/// <summary>
/// <see cref="IShellExecutionGate"/> 的宿主实现：结合 <c>Shell:AllowShellExecution</c> 配置开关
/// 与当前用户角色（默认仅 Admin）判定是否允许 <c>RunInShell=true</c> 的高危 Shell 执行。
/// </summary>
public sealed class ShellExecutionGate : IShellExecutionGate
{
    private readonly IOptions<ShellOptions> _options;
    private readonly IUserContext _userContext;

    /// <summary>
    /// 初始化 Shell 执行门禁。
    /// </summary>
    public ShellExecutionGate(
        IOptions<ShellOptions> options,
        IUserContext userContext)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _userContext = userContext ?? throw new ArgumentNullException(nameof(userContext));
    }

    /// <inheritdoc />
    public Task<bool> IsShellExecutionAllowedAsync(CancellationToken cancellationToken = default)
    {
        var options = _options.Value;

        // 开关未开启则一律禁止（安全默认）。
        if (!options.AllowShellExecution)
        {
            return Task.FromResult(false);
        }

        var roles = _userContext.Roles;
        if (roles is null || roles.Count == 0)
        {
            return Task.FromResult(false);
        }

        var allowed = options.AllowedRoles
            .Select(r => r.Trim())
            .Where(r => r.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var permitted = roles.Any(r => allowed.Contains(r));
        return Task.FromResult(permitted);
    }
}

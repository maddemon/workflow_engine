using System;
using System.Threading;
using System.Threading.Tasks;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;

namespace FlowEngine.Infrastructure.Services;

/// <summary>
/// 凭据解析服务实现，包装 <see cref="ICredentialAccessor"/>：优先按 Guid ID 精确解析，
/// 否则按名称解析（覆盖 dry-run 等临时凭据场景）。
/// </summary>
public sealed class NodeCredentialService(ICredentialAccessor accessor) : ICredentialService
{
    /// <inheritdoc />
    public async Task<CredentialValue?> ResolveAsync(string? idOrName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(idOrName))
        {
            return null;
        }

        // 优先按 Guid ID 解析（精确匹配）。
        if (Guid.TryParse(idOrName, out var id))
        {
            var byId = await accessor.GetCredentialAsync(id, ct).ConfigureAwait(false);
            if (byId is not null)
            {
                return byId;
            }
        }

        // 回退按名称解析。
        return await accessor.GetCredentialByNameAsync(idOrName, ct).ConfigureAwait(false);
    }
}

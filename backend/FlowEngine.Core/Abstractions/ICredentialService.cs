using System.Threading;
using System.Threading.Tasks;
using FlowEngine.Core.Entities;

namespace FlowEngine.Core.Abstractions;

/// <summary>
/// 凭据解析服务：将凭据 ID 或名称解析为凭据值，作为节点直接依赖的抽象，
/// 取代经 <see cref="NodeExecutionContext"/> 服务定位器获取凭据的方式，便于 Phase 4 节点迁移。
/// </summary>
public interface ICredentialService
{
    /// <summary>按 ID 或名称解析凭据值。</summary>
    /// <param name="idOrName">凭据 ID（Guid 字符串）或凭据名称。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>解析到的凭据值；未找到时返回 null。</returns>
    Task<CredentialValue?> ResolveAsync(string? idOrName, CancellationToken ct = default);
}

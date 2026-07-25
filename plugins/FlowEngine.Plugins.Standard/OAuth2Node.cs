using System.ComponentModel;
using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Attributes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;

namespace FlowEngine.Plugins.Standard;

/// <summary>
/// OAuth2 节点：将凭据层已托管（已缓存/刷新）的令牌物化为工作流变量。
/// 不自行请求 token，仅消费 <see cref="ICredentialAccessor"/> 返回的 accessToken。
/// </summary>
public sealed class OAuth2Node : INodeType
{
    /// <inheritdoc />
    public string TypeName => "oauth2";

    /// <inheritdoc />
    public string DisplayName => "OAuth2";

    /// <inheritdoc />
    public string Category => "Network";

    /// <inheritdoc />
    public string Icon => "key";

    /// <inheritdoc />
    public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;

    /// <summary>
    /// 凭据名称。
    /// </summary>
    [DisplayName("Credential Name")]
    [Description("Name of the oauth2 credential to materialize.")]
    public string CredentialName { get; set; } = string.Empty;

    /// <inheritdoc />
    public IReadOnlyList<PortDefinition> Ports { get; } =
    [
        new PortDefinition { Name = FlowConstants.PortNames.Input, DisplayName = "Input", Direction = PortDirection.Input, Type = PortType.Main },
        new PortDefinition { Name = FlowConstants.PortNames.Output, DisplayName = "Output", Direction = PortDirection.Output, Type = PortType.Main }
    ];

    /// <inheritdoc />
    public bool DefaultIsEntry => false;

    /// <inheritdoc />
    public async Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(CredentialName))
        {
            return context.ErrorResult("MissingCredentialName", "CredentialName is required.");
        }

        var credential = await context.ResolveCredentialAsync(CredentialName, cancellationToken)
            .ConfigureAwait(false);

        if (credential is null)
        {
            return context.ErrorResult("CredentialNotFound", $"Credential '{CredentialName}' not found.");
        }

        if (!credential.Fields.TryGetValue("accessToken", out var accessToken) || string.IsNullOrWhiteSpace(accessToken))
        {
            return context.ErrorResult("MissingAccessToken", $"Credential '{CredentialName}' does not contain an accessToken.");
        }

        var tokenType = credential.Fields.TryGetValue("tokenType", out var tt) ? tt : "Bearer";
        DateTime? expiresAt = null;
        if (credential.Fields.TryGetValue("expiresAt", out var expiresAtStr) &&
            DateTime.TryParse(expiresAtStr, out var parsed))
        {
            expiresAt = parsed.ToUniversalTime();
        }

        var data = new JsonObject
        {
            ["accessToken"] = accessToken,
            ["tokenType"] = tokenType,
            ["expiresAt"] = expiresAt.HasValue ? expiresAt.Value.ToString("O") : null
        };

        return context.Ok(data);
    }
}

using System.ComponentModel;
using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Attributes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Exceptions;

namespace FlowEngine.Plugins.Standard;

/// <summary>
/// OAuth2 节点：将凭据层已托管（已缓存/刷新）的令牌物化为工作流变量。
/// 不自行请求 token，仅消费 <see cref="ICredentialAccessor"/> 返回的 accessToken。
/// </summary>
[NodeMeta(TypeName = "oauth2", DisplayName = "OAuth2", Category = NodeCategory.Network, Icon = "key", DefaultIsEntry = false)]
[Port(FlowConstants.PortNames.Input, "Input", PortDirection.Input, PortType.Main)]
[Port(FlowConstants.PortNames.Output, "Output", PortDirection.Output, PortType.Main)]
public sealed class OAuth2Node : NodeBase
{
    [Inject] public ICredentialAccessor Creds { get; private set; } = null!;

    /// <summary>
    /// 凭据名称。
    /// </summary>
    [DisplayName("Credential Name")]
    [Description("Name of the oauth2 credential to materialize.")]
    public string CredentialName { get; set; } = string.Empty;

    /// <inheritdoc />
    public override async Task<NodeHandlerOutput> ExecuteAsync(NodeInput input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(CredentialName))
        {
            throw new NodeExecutionException("MissingCredentialName", "CredentialName is required.");
        }

        var credential = await Creds.ResolveAsync(CredentialName, ct)
            .ConfigureAwait(false);

        if (credential is null)
        {
            throw new NodeExecutionException("CredentialNotFound", $"Credential '{CredentialName}' not found.");
        }

        if (!credential.Fields.TryGetValue("accessToken", out var accessToken) || string.IsNullOrWhiteSpace(accessToken))
        {
            throw new NodeExecutionException("MissingAccessToken", $"Credential '{CredentialName}' does not contain an accessToken.");
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

        return Single(data);
    }

    /// <summary>
    /// 构造单数据项的成功输出。
    /// </summary>
    private static NodeHandlerOutput Single(JsonNode? data) =>
        NodeHandlerOutput.Data(new DataBatch
        {
            Items =
            [
                new DataItem
                {
                    Data = data,
                    Success = true,
                    SourceIndex = 0
                }
            ]
        });
}

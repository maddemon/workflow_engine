using System.ComponentModel;
using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Attributes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;

namespace FlowEngine.Plugins.Standard;

/// <summary>
/// LLM 供应节点，集中管理模型配置并通过供应端口向消费节点提供 LLM 客户端实例。
/// </summary>
public sealed class LlmNode : INodeType
{
    /// <inheritdoc />
    public string TypeName => "llm";

    /// <inheritdoc />
    public string DisplayName => "LLM";

    /// <inheritdoc />
    public string Category => "AI";

    /// <inheritdoc />
    public string Icon => "brain";

    /// <inheritdoc />
    public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;

    /// <summary>
    /// 模型名称（如 gpt-4、gpt-3.5-turbo）。
    /// </summary>
    [Description("LLM model name (e.g. gpt-4, gpt-3.5-turbo).")]
    public string Model { get; set; } = "gpt-4";

    /// <summary>
    /// 温度参数，控制输出随机性（0-2）。
    /// </summary>
    [Description("Temperature parameter controlling output randomness (0-2).")]
    public float Temperature { get; set; } = 0.7f;

    /// <summary>
    /// 最大输出 token 数。
    /// </summary>
    [Description("Maximum number of tokens in the response. Empty means no limit.")]
    public int? MaxTokens { get; set; }

    /// <summary>
    /// API 凭据 ID，用于注入 API Key。
    /// </summary>
    [Credential(FlowConstants.CredentialFields.ApiKey)]
    [Description("Credential ID for API Key injection.")]
    public string? CredentialId { get; set; }

    /// <summary>
    /// API 基础端点（可选，用于自定义或兼容端点）。
    /// </summary>
    [Description("Base endpoint URL for API calls (optional, for custom or compatible endpoints).")]
    public string? BaseEndpoint { get; set; }

    /// <inheritdoc />
    public IReadOnlyList<PortDefinition> Ports { get; } =
    [
        new PortDefinition { Name = FlowConstants.PortNames.Llm, DisplayName = "LLM", Direction = PortDirection.Output, Type = PortType.LLM }
    ];

    /// <inheritdoc />
    public bool DefaultIsEntry => true;

    /// <inheritdoc />
    public async Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(Model))
        {
            return context.ErrorResult("MissingModel", "Model name is required.");
        }

        var apiKey = await ResolveApiKeyAsync(context, cancellationToken).ConfigureAwait(false);
        if (apiKey is null)
        {
            return context.ErrorResult("MissingApiKey", "API Key not available. Configure a valid credential.");
        }

        Uri? endpoint = null;
        if (!string.IsNullOrWhiteSpace(BaseEndpoint) && Uri.TryCreate(BaseEndpoint, UriKind.Absolute, out var uri))
        {
            endpoint = uri;
        }

        ILlmClient llmClient;
        try
        {
            llmClient = new OpenAiLlmClient(
                apiKey: apiKey,
                model: Model,
                temperature: Temperature,
                maxTokens: MaxTokens,
                baseEndpoint: endpoint);
        }
        catch (Exception ex)
        {
            return context.ErrorResult("LlmClientCreationFailed", $"Failed to create LLM client: {ex.Message}");
        }

        context.LlmClient = llmClient;

        return new NodeExecutionResult
        {
            Success = true,
            Output = new DataBatch
            {
                Items =
                [
                    new DataItem
                    {
                        Data = new JsonObject
                        {
                            ["model"] = Model,
                            ["status"] = "ready"
                        },
                        Success = true,
                        SourceIndex = 0
                    }
                ]
            }
        };
    }

    private async Task<string?> ResolveApiKeyAsync(NodeExecutionContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(CredentialId))
        {
            return null;
        }

        if (!Guid.TryParse(CredentialId, out var credentialId))
        {
            return null;
        }

        try
        {
            var credential = await context.Credentials.GetCredentialAsync(credentialId, cancellationToken)
                .ConfigureAwait(false);

            if (credential.Fields.TryGetValue(FlowConstants.CredentialFields.ApiKey, out var apiKey))
            {
                return apiKey;
            }

            return null;
        }
        catch (Exception ex)
        {
            context.Logger?.LogError(ex, "Failed to resolve API key from credential {CredentialId}.", CredentialId);
            return null;
        }
    }
}

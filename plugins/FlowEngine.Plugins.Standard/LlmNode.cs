using System.ComponentModel;
using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Ai;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Attributes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;


namespace FlowEngine.Plugins.Standard;

/// <summary>
/// LLM 供应节点，集中管理模型配置并通过供应端口向消费节点提供 LLM 客户端实例。
/// </summary>
public sealed class LlmNode : INodeType, IAiDefinitionProvider
{
    /// <inheritdoc />
    public string TypeName => "llm";

    /// <inheritdoc />
    public AiNodeDefinition GetAiDefinition(NodeTypeDescriptor descriptor) =>
        AiDefinitionHelpers.Def(
            "LLM", "AI", false,
            "调用大语言模型处理文本/数据。常用于摘要、抽取、分类、翻译。非触发器节点，需放在触发器或上游节点之后。",
            ["ai", "llm", "transform"],
            JsonNode.Parse("""{"type":"object","properties":{"text":{"description":"模型输出文本"}}}"""),
            AiDefinitionHelpers.Example("用 LLM 归类交易",
                JsonNode.Parse("""{"prompt":"将以下流水按类型归类：工资/转账/消费","model":"gpt-4o"}"""),
                JsonNode.Parse("""{"text":"工资: 2 笔; 消费: 5 笔"}""")));

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

        var credential = await context.ResolveCredentialAsync(CredentialId, cancellationToken).ConfigureAwait(false);
        var apiKey = credential?.Fields?.TryGetValue(FlowConstants.CredentialFields.ApiKey, out var key) == true ? key : null;
        if (apiKey is null)
        {
            return context.ErrorResult("MissingApiKey", "API Key not available. Configure a valid credential.");
        }

        Uri? endpoint = null;
        if (!string.IsNullOrWhiteSpace(BaseEndpoint) && Uri.TryCreate(BaseEndpoint, UriKind.Absolute, out var uri))
        {
            var ssrfGuard = context.GuardSsrf(BaseEndpoint);
            if (ssrfGuard is not null) return ssrfGuard;

            endpoint = uri;
        }

        if (context.LlmClientFactory is null)
        {
            return context.ErrorResult(
                "LlmClientFactoryUnavailable",
                "LLM client factory is not available in the execution context.");
        }

        ILlmClient llmClient;
        try
        {
            llmClient = context.LlmClientFactory.Create(
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

        return context.Ok(new JsonObject
        {
            ["model"] = Model,
            ["status"] = "ready"
        });
    }

}

using System.ComponentModel;
using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Ai;
using FlowEngine.Core.Attributes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Exceptions;

namespace FlowEngine.Plugins.Standard;

/// <summary>
/// LLM 供应节点，集中管理模型配置并通过供应端口向消费节点提供 LLM 客户端实例。
/// 在新模型中，LLM 客户端由框架依据凭据解析并经受保护属性 <see cref="NodeBase.LlmClient"/> 注入；
/// 本节点负责校验模型/凭据并向执行上下文发布该客户端，供下游 LLM 消费节点复用。
/// </summary>
[NodeMeta(TypeName = "llm", DisplayName = "LLM", Category = NodeCategory.AI, Icon = "brain", DefaultIsEntry = true)]
[Port(FlowConstants.PortNames.Llm, "LLM", PortDirection.Output, PortType.LLM)]
public sealed class LlmNode : NodeBase
{
    [Inject] public ILlmClient? LlmClient { get; private set; }
    [Inject] public NodeExecutionContext Ctx { get; private set; } = null!;
    [Inject] public ICredentialAccessor Creds { get; private set; } = null!;
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
    protected override AiNodeDefinition? GetAiDefinition(NodeTypeDescriptor descriptor) =>
        AiDefinitionHelpers.Def(
            "LLM", "AI", false,
            "调用大语言模型处理文本/数据。常用于摘要、抽取、分类、翻译。非触发器节点，需放在触发器或上游节点之后。",
            ["ai", "llm", "transform"],
            JsonNode.Parse("{\"type\":\"object\",\"properties\":{\"text\":{\"description\":\"模型输出文本\"}}}"),
            AiDefinitionHelpers.Example("用 LLM 归类交易",
                JsonNode.Parse("{\"prompt\":\"将以下流水按类型归类：工资/转账/消费\",\"model\":\"gpt-4o\"}"),
                JsonNode.Parse("{\"text\":\"工资: 2 笔; 消费: 5 笔\"}")));

    /// <inheritdoc />
    public override async Task<NodeHandlerOutput> ExecuteAsync(NodeInput input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(Model))
        {
            throw new NodeExecutionException("MissingModel", "Model name is required.");
        }

        var credential = await Creds.ResolveAsync(CredentialId, ct).ConfigureAwait(false);
        var apiKey = credential?.Fields?.TryGetValue(FlowConstants.CredentialFields.ApiKey, out var key) == true ? key : null;
        if (apiKey is null)
        {
            throw new NodeExecutionException("MissingApiKey", "API Key not available. Configure a valid credential.");
        }

        Uri? endpoint = null;
        if (!string.IsNullOrWhiteSpace(BaseEndpoint) && Uri.TryCreate(BaseEndpoint, UriKind.Absolute, out var uri))
        {
            var ssrfBlock = Ctx.GuardSsrf(BaseEndpoint);
            if (ssrfBlock is not null)
            {
                throw new NodeExecutionException(ssrfBlock.Error!.Code, ssrfBlock.Error.Message);
            }

            endpoint = uri;
        }

        // 新模型：LLM 客户端由框架依据凭据解析并注入（受保护属性 LlmClient），
        // 本节点负责校验其可用，并发布到执行上下文供下游 LLM 消费节点复用。
        if (LlmClient is null)
        {
            throw new NodeExecutionException(FlowConstants.ErrorCodes.MissingLlmClient, "LLM client not available.");
        }

        // 框架已在 ExecutionStage 解析并经 [Inject] 注入 LlmClient，并写入 Ctx.LlmClient 供下游复用，此处无需回写。
        return Single(new JsonObject
        {
            ["model"] = Model,
            ["status"] = "ready"
        });
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
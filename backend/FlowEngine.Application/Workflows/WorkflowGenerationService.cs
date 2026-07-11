using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Configuration;
using FlowEngine.Core.Entities;
using Microsoft.Extensions.Logging;

namespace FlowEngine.Application.Workflows;

/// <summary>
/// 工作流生成请求。
/// </summary>
/// <param name="Description">自然语言描述。</param>
/// <param name="ProjectId">可选的项目 ID。</param>
/// <param name="MaxRetries">覆盖 <see cref="AiOptions.MaxRetries"/> 的最大纠错重试次数。</param>
public sealed record WorkflowGenerationRequest(
    string Description,
    Guid? ProjectId = null,
    int? MaxRetries = null);

/// <summary>
/// 工作流生成响应。
/// </summary>
/// <param name="Valid">生成结果是否通过结构化校验。</param>
/// <param name="Draft">最后一次生成的工作流 JSON（可能为 null）。</param>
/// <param name="Errors">校验错误清单（Valid 为 false 时有意义）。</param>
/// <param name="Attempts">总尝试次数（含首次生成与纠错重试）。</param>
public sealed record WorkflowGenerationResponse(
    bool Valid,
    JsonNode? Draft,
    IReadOnlyList<string> Errors,
    int Attempts);

/// <summary>
/// 工作流语义解析服务：构造 Prompt → 调用 LLM → 解析 JSON → 结构化校验 → 纠错循环。
/// 依赖系统级 <see cref="ILlmClient"/>（来自 appsettings 的 Ai 配置）与 <see cref="WorkflowDraftValidator"/>。
/// </summary>
public sealed class WorkflowGenerationService(
    Func<ILlmClient> llmClientFactory,
    INodeRegistry nodeRegistry,
    WorkflowDraftValidator validator,
    AiOptions options,
    ILogger<WorkflowGenerationService> logger)
{
    /// <summary>
    /// 根据自然语言描述生成工作流草案。
    /// </summary>
    /// <param name="request">生成请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>生成结果（含草案、是否合法、错误清单与尝试次数）。</returns>
    public async Task<WorkflowGenerationResponse> GenerateAsync(
        WorkflowGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Description))
        {
            return new WorkflowGenerationResponse(false, null, ["描述不能为空"], 0);
        }

        var messages = new List<LlmMessage>
        {
            new() { Role = "system", Content = PromptTemplates.BuildSystemPrompt(nodeRegistry) },
            new() { Role = "user", Content = request.Description },
        };

        var maxRetries = request.MaxRetries ?? options.MaxRetries;
        var errors = new List<string>();
        JsonNode? lastDraft = null;

        // 延迟解析 LLM 客户端：仅当真正生成时才触发解析，
        // 若 Ai:ApiKey 未配置，SystemLlmClientFactory 会在此抛出友好错误（不阻塞宿主启动，也不影响其它端点）。
        var llmClient = llmClientFactory();

        // 首次生成 + 最多 maxRetries 次纠错重试。
        for (var attempt = 1; attempt <= maxRetries + 1; attempt++)
        {
            LlmResponse response;
            try
            {
                response = await llmClient.ChatAsync(messages, [], cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "LLM 调用失败（第 {Attempt} 次尝试）", attempt);
                errors.Add($"LLM 调用失败：{ex.Message}");
                return new WorkflowGenerationResponse(false, lastDraft, errors, attempt);
            }

            var draft = ParseDraft(response.Content);
            if (draft is null)
            {
                errors.Add("LLM 输出不是合法的 JSON");
                messages.Add(new LlmMessage { Role = "assistant", Content = response.Content });
                messages.Add(new LlmMessage
                {
                    Role = "user",
                    Content = "你上一次的输出不是合法的 JSON 对象，请仅输出 JSON，不要任何解释或 markdown 包裹。",
                });
                continue;
            }

            lastDraft = draft;
            var validation = await validator.ValidateAsync(draft, cancellationToken).ConfigureAwait(false);
            if (validation.Valid)
            {
                return new WorkflowGenerationResponse(true, draft, [], attempt);
            }

            errors = validation.Errors.ToList();
            messages.Add(new LlmMessage { Role = "assistant", Content = response.Content });
            messages.Add(new LlmMessage
            {
                Role = "user",
                Content = PromptTemplates.BuildCorrectionMessage(validation.Errors),
            });
        }

        return new WorkflowGenerationResponse(false, lastDraft, errors, maxRetries + 1);
    }

    /// <summary>
    /// 将 LLM 文本输出解析为 JSON，去除可能的 markdown 代码围栏。
    /// </summary>
    private static JsonNode? ParseDraft(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        var text = content!.Trim();
        if (text.StartsWith("```", System.StringComparison.Ordinal))
        {
            var firstNewline = text.IndexOf('\n');
            var endFence = text.LastIndexOf("```", System.StringComparison.Ordinal);
            if (firstNewline >= 0 && endFence > firstNewline)
            {
                text = text.Substring(firstNewline + 1, endFence - firstNewline - 1).Trim();
            }
        }

        try
        {
            return JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

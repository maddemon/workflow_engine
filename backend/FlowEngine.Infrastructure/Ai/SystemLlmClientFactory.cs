using System;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Configuration;

namespace FlowEngine.Infrastructure.Ai;

/// <summary>
/// 系统级 LLM 客户端工厂，依据 <see cref="AiOptions"/> 配置创建 <see cref="ILlmClient"/> 实例。
/// 注册为 DI 中的单例 <see cref="ILlmClient"/>，供后端语义解析服务（如 AI DSL 生成）复用。
/// </summary>
public static class SystemLlmClientFactory
{
    /// <summary>
    /// 根据配置创建 LLM 客户端。
    /// </summary>
    /// <param name="options">LLM 配置（来自 appsettings 的 <c>Ai</c> 节点）。</param>
    /// <returns>已配置的 <see cref="ILlmClient"/> 实例。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> 为 null 时抛出。</exception>
    /// <exception cref="InvalidOperationException">
    /// <see cref="AiOptions.ApiKey"/> 未配置，或 <see cref="AiOptions.BaseEndpoint"/> 不是合法绝对 URI 时抛出。
    /// </exception>
    public static ILlmClient Create(AiOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new InvalidOperationException(
                "LLM 系统级集成未配置：请在 appsettings 的 \"Ai:ApiKey\" 中设置 API Key，" +
                "或通过环境变量 Ai__ApiKey 注入。未配置时无法使用 AI DSL 生成等依赖 LLM 的功能。");
        }

        Uri? endpoint = null;
        if (!string.IsNullOrWhiteSpace(options.BaseEndpoint)
            && !Uri.TryCreate(options.BaseEndpoint, UriKind.Absolute, out endpoint))
        {
            throw new InvalidOperationException(
                $"LLM 基础端点配置无效：\"{options.BaseEndpoint}\" 不是合法的绝对 URI。");
        }

        return new OpenAiLlmClient(
            apiKey: options.ApiKey,
            model: options.Model,
            temperature: options.Temperature,
            maxTokens: options.MaxTokens,
            baseEndpoint: endpoint);
    }
}

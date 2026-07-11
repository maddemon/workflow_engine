using FlowEngine.Core.Entities;

namespace FlowEngine.Core.Abstractions;

/// <summary>
/// LLM 客户端工厂契约，依据运行时参数创建 <see cref="ILlmClient"/> 实例。
/// 定义于 Core，使插件（如 <c>LlmNode</c>）能够在不依赖 Infrastructure 具体实现的前提下创建客户端，
/// 满足“插件仅依赖 Core”的边界约束。
/// </summary>
public interface ILlmClientFactory
{
    /// <summary>
    /// 创建 LLM 客户端。
    /// </summary>
    /// <param name="apiKey">API Key（必填）。</param>
    /// <param name="model">模型名称，如 gpt-4。</param>
    /// <param name="temperature">温度参数（0-2）。</param>
    /// <param name="maxTokens">最大输出 token 数（可选，为空表示不限制）。</param>
    /// <param name="baseEndpoint">API 基础端点（可选，用于自定义或兼容端点）。</param>
    /// <returns>已配置的 <see cref="ILlmClient"/> 实例。</returns>
    ILlmClient Create(
        string apiKey,
        string model,
        float temperature = 0.7f,
        int? maxTokens = null,
        Uri? baseEndpoint = null);
}

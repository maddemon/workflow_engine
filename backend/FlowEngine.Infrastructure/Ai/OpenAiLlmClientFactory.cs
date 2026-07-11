using FlowEngine.Core.Abstractions;

namespace FlowEngine.Infrastructure.Ai;

/// <summary>
/// <see cref="OpenAiLlmClient"/> 工厂，封装按运行时参数创建 OpenAI 兼容客户端的逻辑。
/// 实现 <see cref="ILlmClientFactory"/>（定义于 Core），供插件层（如 <c>LlmNode</c>）通过
/// <see cref="FlowEngine.Core.Entities.NodeExecutionContext.LlmClientFactory"/> 解析使用，
/// 避免插件直接依赖 Infrastructure 具体类型，满足插件边界约束。
/// </summary>
public sealed class OpenAiLlmClientFactory : ILlmClientFactory
{
    /// <inheritdoc />
    public ILlmClient Create(
        string apiKey,
        string model,
        float temperature = 0.7f,
        int? maxTokens = null,
        Uri? baseEndpoint = null)
    {
        return new OpenAiLlmClient(
            apiKey: apiKey,
            model: model,
            temperature: temperature,
            maxTokens: maxTokens,
            baseEndpoint: baseEndpoint);
    }
}

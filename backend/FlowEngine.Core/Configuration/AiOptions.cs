namespace FlowEngine.Core.Configuration;

/// <summary>
/// LLM 系统级配置，对应 appsettings.json 的 <c>Ai</c> 节点。
/// 用于后端（如 AI DSL 生成服务）通过配置而非硬编码方式调用 LLM。
/// </summary>
public sealed class AiOptions
{
    /// <summary>
    /// 配置节点名称。
    /// </summary>
    public const string SectionName = "Ai";

    /// <summary>
    /// API Key。未配置（空）时系统级 LLM 客户端不可用，
    /// 解析 <see cref="FlowEngine.Core.Abstractions.ILlmClient"/> 会抛出友好错误。
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// 模型名称，如 gpt-4o、gpt-4o-mini。
    /// </summary>
    public string Model { get; set; } = "gpt-4o";

    /// <summary>
    /// 温度参数（0-2），控制输出随机性。DSL 生成建议偏低（0.3）以保证稳定。
    /// </summary>
    public float Temperature { get; set; } = 0.3f;

    /// <summary>
    /// 最大输出 token 数；<c>null</c> 表示不限制。
    /// </summary>
    public int? MaxTokens { get; set; }

    /// <summary>
    /// API 基础端点（可选），用于自定义或兼容端点（如私有化部署）。
    /// 为空时使用 SDK 默认端点。
    /// </summary>
    public string? BaseEndpoint { get; set; }

    /// <summary>
    /// 生成工作流时的最大纠错重试次数（不含首次生成）。默认 3 次。
    /// </summary>
    public int MaxRetries { get; set; } = 3;
}

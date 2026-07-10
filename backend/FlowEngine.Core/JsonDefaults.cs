using System.Text.Json;

namespace FlowEngine.Core;

/// <summary>
/// 全项目共享的 <see cref="JsonSerializerOptions"/> 默认配置，避免各模块重复实例化。
/// </summary>
public static class JsonDefaults
{
    /// <summary>
    /// 标准选项：camelCase 命名策略，非缩进。
    /// </summary>
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };
}

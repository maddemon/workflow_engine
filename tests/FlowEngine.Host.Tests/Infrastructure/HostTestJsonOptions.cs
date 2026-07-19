using System.Text.Json;
using System.Text.Json.Serialization;

namespace FlowEngine.Host.Tests.Infrastructure;

/// <summary>
/// Host 集成测试统一的 JSON 反序列化选项，与生产环境 MVC 配置保持一致（驼峰命名 + 字符串枚举）。
/// </summary>
public static class HostTestJsonOptions
{
    public static JsonSerializerOptions Default { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };
}

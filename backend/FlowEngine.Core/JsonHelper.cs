using System.Text.Json;

namespace FlowEngine.Core;

/// <summary>
/// 通用 JSON 反序列化辅助（与节点 / 执行上下文无关），供节点以静态方式解析动态 JSON 串。
/// 逻辑原位于 <see cref="Abstractions.NodeBase.TryParseJson{T}"/> 与
/// <see cref="Entities.NodeExecutionContextExtensions.TryParseJson{T}"/>，本次纯重构下沉为静态工具类
/// （设计文档 §5 步骤 5：方法 helper 各回各家）。
/// </summary>
public static class JsonHelper
{
    /// <summary>
    /// 尝试将 JSON 字符串反序列化为强类型对象。失败（非法 JSON 或反序列化为 null）时返回 false，
    /// 调用方据此分支处理。
    /// </summary>
    /// <typeparam name="T">目标类型。</typeparam>
    /// <param name="raw">原始 JSON 字符串。</param>
    /// <param name="result">反序列化结果；失败时置为 <c>default</c>。</param>
    /// <param name="errorCode">失败时的错误码（"InvalidJson"）。</param>
    /// <param name="opts">序列化选项，可空。</param>
    /// <returns>是否成功。</returns>
    public static bool TryParse<T>(string raw, out T? result, out string? errorCode, JsonSerializerOptions? opts = null)
    {
        try
        {
            result = JsonSerializer.Deserialize<T>(raw, opts);
            if (result is null)
            {
                errorCode = "InvalidJson";
                return false;
            }

            errorCode = null;
            return true;
        }
        catch (JsonException)
        {
            result = default;
            errorCode = "InvalidJson";
            return false;
        }
    }
}

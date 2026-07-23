using System.Text.Json.Nodes;

namespace FlowEngine.Core.Tools;

/// <summary>
/// 节点通用数据辅助方法。集中处理“从输入 JSON 字段取出 base64 并解码为字节数组”的重复逻辑，
/// 避免各节点（压缩/写文件/电子表格/邮件附件等）重复实现取值与解码。
/// </summary>
public static class NodeDataHelpers
{
    /// <summary>
    /// 从 <paramref name="data"/> 的指定字段取出 base64 字符串并解码为字节数组。
    /// </summary>
    /// <param name="data">输入数据对象（可为 null）。</param>
    /// <param name="fieldName">字段名。</param>
    /// <param name="bytes">解码后的字节数组；失败时为空数组（不会为 null）。</param>
    /// <returns>
    /// <see cref="Base64FieldResult.Success"/> 解码成功；
    /// <see cref="Base64FieldResult.Missing"/> 字段缺失、非字符串或值为 null；
    /// <see cref="Base64FieldResult.Invalid"/> base64 语法非法。
    /// </returns>
    public static Base64FieldResult TryGetBase64Field(JsonNode? data, string fieldName, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();

        if (data is not JsonObject obj)
        {
            return Base64FieldResult.Missing;
        }

        if (obj[fieldName] is not JsonValue value)
        {
            return Base64FieldResult.Missing;
        }

        if (!value.TryGetValue<string>(out var base64) || base64 is null)
        {
            return Base64FieldResult.Missing;
        }

        try
        {
            bytes = Convert.FromBase64String(base64);
            return Base64FieldResult.Success;
        }
        catch (FormatException)
        {
            return Base64FieldResult.Invalid;
        }
    }

    /// <summary>
    /// <see cref="TryGetBase64Field"/> 的结果分类。
    /// </summary>
    public enum Base64FieldResult
    {
        /// <summary>解码成功。</summary>
        Success,

        /// <summary>字段缺失、非字符串或值为 null。</summary>
        Missing,

        /// <summary>base64 语法非法。</summary>
        Invalid
    }
}

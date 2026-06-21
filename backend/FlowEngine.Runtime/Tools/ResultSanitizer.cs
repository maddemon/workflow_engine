using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace FlowEngine.Runtime.Tools;

/// <summary>
/// 工具结果消毒器，对工具执行结果进行安全处理。
/// </summary>
public static partial class ResultSanitizer
{
    private const int DefaultMaxResultLength = 32_768;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly string[] InjectionPatterns =
    [
        "ignore previous instructions",
        "ignore all previous instructions",
        "disregard previous instructions",
        "disregard all previous instructions",
        "forget your instructions",
        "forget your previous instructions",
        "you are now",
        "new instructions:",
        "override your instructions",
        "bypass your instructions"
    ];

    private static readonly Regex CredentialPattern = CredentialRegex();
    private static readonly Regex PrivateKeyPattern = PrivateKeyRegex();
    private static readonly Regex BearerTokenPattern = BearerTokenRegex();

    /// <summary>
    /// 对工具结果进行完整消毒流程：截断、注入过滤、敏感信息移除、结构化包装。
    /// </summary>
    public static string Sanitize(string toolName, string result, int maxLength = DefaultMaxResultLength)
    {
        var sanitized = result;
        sanitized = FilterInjectionPatterns(sanitized);
        sanitized = SanitizeCredentials(sanitized);
        sanitized = Truncate(sanitized, maxLength);
        return WrapStructured(toolName, sanitized);
    }

    /// <summary>
    /// 截断超长结果并附加省略说明。
    /// </summary>
    public static string Truncate(string result, int maxLength = DefaultMaxResultLength)
    {
        if (string.IsNullOrEmpty(result) || result.Length <= maxLength)
        {
            return result;
        }

        return result[..maxLength] + $"\n\n[Result truncated at {maxLength} characters. Original length: {result.Length}]";
    }

    /// <summary>
    /// 过滤已知 prompt injection 模式。
    /// </summary>
    public static string FilterInjectionPatterns(string result)
    {
        if (string.IsNullOrEmpty(result))
        {
            return result;
        }

        var modified = result;
        foreach (var pattern in InjectionPatterns)
        {
            if (modified.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                modified = modified.Replace(pattern, "[FILTERED]", StringComparison.OrdinalIgnoreCase);
            }
        }

        return modified;
    }

    /// <summary>
    /// 结构化包装：用 JSON 包裹工具结果，标注来源与类型。
    /// </summary>
    public static string WrapStructured(string toolName, string result)
    {
        var wrapper = new JsonObject
        {
            ["tool"] = toolName,
            ["result"] = result,
            ["truncated"] = result.Length > DefaultMaxResultLength
        };

        return wrapper.ToJsonString(JsonOptions);
    }

    /// <summary>
    /// 移除结果中可能包含的凭据、Token、私钥。
    /// </summary>
    public static string SanitizeCredentials(string result)
    {
        if (string.IsNullOrEmpty(result))
        {
            return result;
        }

        var sanitized = CredentialPattern.Replace(result, "[CREDENTIAL_REMOVED]");
        sanitized = PrivateKeyPattern.Replace(sanitized, "[PRIVATE_KEY_REMOVED]");
        sanitized = BearerTokenPattern.Replace(sanitized, "Bearer [TOKEN_REMOVED]");

        return sanitized;
    }

    [GeneratedRegex(@"(?:api[_-]?key|apikey|secret[_-]?key|access[_-]?token|auth[_-]?token)\s*[:=]\s*['""]?[\w\-\.]{16,}['""]?", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex CredentialRegex();

    [GeneratedRegex(@"-----BEGIN\s+(?:RSA\s+)?PRIVATE\s+KEY-----[\s\S]*?-----END\s+(?:RSA\s+)?PRIVATE\s+KEY-----", RegexOptions.Compiled)]
    private static partial Regex PrivateKeyRegex();

    [GeneratedRegex(@"Bearer\s+[\w\-\.]{20,}", RegexOptions.Compiled)]
    private static partial Regex BearerTokenRegex();
}

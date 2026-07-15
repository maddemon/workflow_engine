using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Exceptions;
using FlowEngine.Core.Scripting;

namespace FlowEngine.Application.Workflows;

/// <summary>
/// 工作流草案校验结果。
/// </summary>
/// <param name="Valid">是否通过校验。</param>
/// <param name="Errors">错误列表；为空表示结构合法。</param>
/// <param name="Warnings">警告列表（不影响 Validity）。</param>
public sealed record DraftValidationResult(
    bool Valid,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);

/// <summary>
/// 工作流 DSL 草案结构化校验器，供 AI 生成服务（阶段三）复用。
/// 校验项：结构（name/nodes/connections）、节点类型、端口方向、连接完整性、
/// 必填参数，以及 DSL 中引用的凭据是否存在（通过 <see cref="ICredentialAccessor"/> 按名称查询）。
/// </summary>
/// <remarks>
/// 设计上直接接受解析后的 <see cref="JsonNode"/>（与 CLI <c>validateWorkflow</c> 语义一致），
/// 避免对 DSL 任意形态做强类型约束；凭据存在性校验为异步（需查询凭据库）。
/// 构造依赖对应计划固化结论：<c>(INodeRegistry, ICredentialService)</c>，
/// 其中代码库实际为 <see cref="ICredentialAccessor"/>（按名称查询）。
/// </remarks>
public sealed class WorkflowDraftValidator(
    INodeRegistry nodeRegistry,
    ICredentialAccessor credentialAccessor)
{
    private static readonly Regex CredentialReferenceRegex =
        new(@"\$credentials\.([A-Za-z0-9_]+)", RegexOptions.Compiled);

    /// <summary>
    /// 校验工作流草案。
    /// </summary>
    /// <param name="draft">解析后的工作流 JSON（顶层对象）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>校验结果（含错误与警告列表）。</returns>
    public async Task<DraftValidationResult> ValidateAsync(
        JsonNode? draft,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (draft is not JsonObject root)
        {
            return new DraftValidationResult(false, ["工作流必须是 JSON 对象"], warnings);
        }

        // ── 名称 ────────────────────────────────────────────────
        var name = GetString(root["name"]);
        if (string.IsNullOrWhiteSpace(name))
        {
            errors.Add("工作流名称不能为空");
        }

        // ── nodes ───────────────────────────────────────────────
        if (root["nodes"] is not JsonArray nodes || nodes.Count == 0)
        {
            errors.Add("nodes 必须是非空数组");
            return new DraftValidationResult(false, errors, warnings);
        }

        // ── connections（可选但须为数组）────────────────────────
        if (root.ContainsKey("connections") && root["connections"] is not JsonArray)
        {
            errors.Add("connections 必须是数组");
        }

        // ── 节点基础校验 + 构建 id→node 映射 ──────────────────────
        var nodeMap = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        var entryCount = 0;
        var triggerCount = 0;
        var nodeIndex = 0;
        foreach (var nodeItem in nodes)
        {
            var prefix = $"nodes[{nodeIndex}]";
            nodeIndex++;
            if (nodeItem is not JsonObject node)
            {
                errors.Add($"{prefix} 必须是对象");
                continue;
            }

            var id = GetString(node["id"]);
            if (string.IsNullOrEmpty(id))
            {
                errors.Add($"{prefix} 缺少有效的 id");
                continue;
            }

            if (nodeMap.ContainsKey(id))
            {
                errors.Add($"节点 id \"{id}\" 重复");
            }
            nodeMap[id] = node;

            if (string.IsNullOrEmpty(GetString(node["typeName"])))
            {
                errors.Add($"{prefix} 缺少有效的 typeName");
                continue;
            }

            if (node["isEntry"]?.GetValueKind() == JsonValueKind.True)
            {
                entryCount++;
            }
        }

        // 入口由后端自动推导（首个 Trigger 节点），因此不强制显式 isEntry。
        // 仅当既无显式入口、也无任何触发器节点时才报错（与 assemble 路径的 Trigger 校验一致）。
        if (entryCount == 0 && triggerCount == 0)
        {
            errors.Add("至少需要一个入口节点（isEntry = true）或触发器节点");
        }

        // ── 节点类型 / 必填参数 / 凭据参数 ───────────────────────
        var descriptors = nodeRegistry.GetDescriptors()
            .ToDictionary(d => d.TypeName, StringComparer.Ordinal);
        var referencedCredentials = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (id, node) in nodeMap)
        {
            var typeName = GetString(node["typeName"])!;
            if (!descriptors.TryGetValue(typeName, out var descriptor))
            {
                errors.Add($"节点 \"{id}\" 使用了未知的节点类型 \"{typeName}\"");
                continue;
            }

            var parameters = node["parameters"] as JsonObject ?? new JsonObject();

            if (descriptor.Category.Equals("Trigger", StringComparison.OrdinalIgnoreCase))
            {
                triggerCount++;
            }

            foreach (var param in descriptor.Parameters)
            {
                var paramValue = parameters.TryGetPropertyValue(param.Name, out var pv) ? pv : null;
                if (IsEmptyValue(paramValue))
                {
                    // 带默认值的参数本质是可选的，AI 不填时使用默认值（task-013 P5a）。
                    if (param.Required && param.DefaultValue is null)
                    {
                        errors.Add($"节点 \"{id}\" ({typeName}) 缺少必填参数 \"{param.Name}\"");
                    }
                    continue;
                }

                // 凭据类型参数：参数值即凭据名称（或 $credentials.<name> 表达式），校验存在性。
                if (!string.IsNullOrEmpty(param.CredentialType) && paramValue is JsonValue cv
                    && cv.GetValueKind() == JsonValueKind.String)
                {
                    var credName = ExtractCredentialName(cv.GetValue<string>()!);
                    if (!string.IsNullOrEmpty(credName))
                    {
                        referencedCredentials.Add(credName);
                    }
                }
            }

            // 扫描参数中的所有 $credentials.<name> 表达式引用。
            CollectCredentialReferences(parameters, referencedCredentials);

            // 表达式参数校验：mustache 词法扫描（首要防线）+ JS 编译检查（通用语法网）。
            CollectMustacheErrors(parameters, id, errors);
            CollectExpressionSyntaxErrors(parameters, descriptor, id, errors);
        }

        // ── 连接完整性 / 端口方向 ───────────────────────────────
        if (root["connections"] is JsonArray connections)
        {
            var connIndex = 0;
            foreach (var connItem in connections)
            {
                var connPrefix = $"connections[{connIndex}]";
                connIndex++;
                if (connItem is not JsonObject conn)
                {
                    errors.Add($"{connPrefix} 必须是对象");
                    continue;
                }

                var sourceId = GetString(conn["sourceNodeId"]) ?? string.Empty;
                var targetId = GetString(conn["targetNodeId"]) ?? string.Empty;
                var sourcePort = GetString(conn["sourcePortName"]) ?? string.Empty;
                var targetPort = GetString(conn["targetPortName"]) ?? string.Empty;

                if (string.IsNullOrEmpty(sourceId) || !nodeMap.ContainsKey(sourceId))
                {
                    errors.Add($"{connPrefix} 引用了不存在的源节点 \"{sourceId}\"");
                    continue;
                }
                if (string.IsNullOrEmpty(targetId) || !nodeMap.ContainsKey(targetId))
                {
                    errors.Add($"{connPrefix} 引用了不存在的目标节点 \"{targetId}\"");
                    continue;
                }

                var sourceType = GetString(nodeMap[sourceId]["typeName"]);
                var targetType = GetString(nodeMap[targetId]["typeName"]);
                if (sourceType is null || targetType is null)
                {
                    continue;
                }

                if (descriptors.TryGetValue(sourceType, out var sd))
                {
                    var port = sd.Ports.FirstOrDefault(p => p.Name == sourcePort);
                    if (port is null)
                    {
                        errors.Add($"{connPrefix} 源节点 \"{sourceId}\" ({sourceType}) 不存在 Output 端口 \"{sourcePort}\"");
                    }
                    else if (port.Direction != PortDirection.Output)
                    {
                        errors.Add($"{connPrefix} 源端口 \"{sourcePort}\" 必须是 Output 端口（当前为 {port.Direction}）");
                    }
                }

                if (descriptors.TryGetValue(targetType, out var td))
                {
                    var port = td.Ports.FirstOrDefault(p => p.Name == targetPort);
                    if (port is null)
                    {
                        errors.Add($"{connPrefix} 目标节点 \"{targetId}\" ({targetType}) 不存在 Input 端口 \"{targetPort}\"");
                    }
                    else if (port.Direction != PortDirection.Input)
                    {
                        errors.Add($"{connPrefix} 目标端口 \"{targetPort}\" 必须是 Input 端口（当前为 {port.Direction}）");
                    }
                }
            }
        }

        // ── 凭据存在性 ──────────────────────────────────────────
        foreach (var credName in referencedCredentials)
        {
            try
            {
                var existing = await credentialAccessor.GetCredentialByNameAsync(credName, cancellationToken)
                    .ConfigureAwait(false);
                if (existing is null)
                {
                    errors.Add($"引用了不存在的凭据 \"{credName}\"，请先通过 credential create 创建");
                }
                else
                {
                    warnings.Add($"凭据 \"{credName}\" 已存在");
                }
            }
            catch (NotFoundException)
            {
                errors.Add($"引用了不存在的凭据 \"{credName}\"，请先通过 credential create 创建");
            }
        }

        return new DraftValidationResult(errors.Count == 0, errors, warnings);
    }

    private static string? GetString(JsonNode? node)
        => node is JsonValue v && v.GetValueKind() == JsonValueKind.String ? v.GetValue<string>() : null;

    private static bool IsEmptyValue(JsonNode? value)
    {
        if (value is null)
        {
            return true;
        }

        var kind = value.GetValueKind();
        if (kind == JsonValueKind.Null || kind == JsonValueKind.Undefined)
        {
            return true;
        }

        if (kind == JsonValueKind.String && string.IsNullOrEmpty(value.GetValue<string>()))
        {
            return true;
        }

        return false;
    }

    private static string? ExtractCredentialName(string raw)
    {
        if (raw.StartsWith("$credentials.", StringComparison.Ordinal))
        {
            var match = CredentialReferenceRegex.Match(raw);
            return match.Success ? match.Groups[1].Value : null;
        }

        return raw;
    }

    public static void CollectCredentialReferences(JsonNode? node, HashSet<string> names)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var prop in obj)
                {
                    CollectCredentialReferences(prop.Value, names);
                }
                break;
            case JsonArray arr:
                foreach (var item in arr)
                {
                    CollectCredentialReferences(item, names);
                }
                break;
            case JsonValue val when val.GetValueKind() == JsonValueKind.String:
                var text = val.GetValue<string>()!;
                foreach (Match m in CredentialReferenceRegex.Matches(text))
                {
                    names.Add(m.Groups[1].Value);
                }
                break;
        }
    }

    /// <summary>
    /// 递归扫描参数字典中的字符串叶子，命中 n8n mustache 标记 {{ / }} 即报错。
    /// 注意：本引擎表达式是 JavaScript，不支持 n8n 的 {{ }} 模板。
    /// 裸写 https://x?t={{...}} 会被 JS 引擎当成 "//" 注释导致编译失败（JS 校验可抓到但报错晦涩）；
    /// 带引号 'https://x?t={{...}}' 则是合法字符串字面量、JS 校验漏报。
    /// 因此词法扫描是首要防线（带/不带引号都命中），JS 编译校验作为通用语法网补充。
    /// </summary>
    public static void CollectMustacheErrors(JsonNode? parameters, string nodeId, List<string> errors)
    {
        if (parameters is null) return;
        ScanMustache(parameters, nodeId, errors, fieldName: null);
    }

    private static void ScanMustache(JsonNode node, string nodeId, List<string> errors, string? fieldName)
    {
        switch (node)
        {
            case JsonValue value:
                if (value.GetValueKind() == System.Text.Json.JsonValueKind.String)
                {
                    var raw = value.GetValue<string>();
                    if (raw.Contains("{{") || raw.Contains("}}"))
                    {
                        var where = fieldName is null
                            ? $"节点 \"{nodeId}\""
                            : $"节点 \"{nodeId}\" 参数 \"{fieldName}\"";
                        errors.Add(
                            $"{where} 含 n8n 风格的 {{{{ }}}} mustache 模板语法，本引擎不支持。" +
                            $"请改用 JavaScript 表达式，例如：'https://api.com/path?token=' + $json.token");
                    }
                }
                break;

            case JsonObject obj:
                foreach (var prop in obj)
                {
                    ScanMustache(prop.Value!, nodeId, errors, prop.Key);
                }
                break;

            case JsonArray arr:
                foreach (var item in arr)
                {
                    ScanMustache(item!, nodeId, errors, fieldName);
                }
                break;
        }
    }

    /// <summary>
    /// 对节点的表达式参数源码跑一次 JS 编译，兜住语法错误。
    /// 仅检查 Hint==Expression 的参数以及 Script/Code 类型的参数。
    /// </summary>
    public static void CollectExpressionSyntaxErrors(JsonNode? parameters, NodeTypeDescriptor descriptor, string nodeId, List<string> errors)
    {
        if (parameters is not JsonObject obj) return;

        var expressionParamNames = descriptor.Parameters
            .Where(p => p.Hint == PresentationHint.Expression || p.Type is ParameterType.Script or ParameterType.Code)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var prop in obj)
        {
            if (!expressionParamNames.Contains(prop.Key)) continue;

            if (prop.Value is JsonValue v && v.GetValueKind() == JsonValueKind.String)
            {
                var raw = v.GetValue<string>();
                if (string.IsNullOrWhiteSpace(raw)) continue;
                if (!ScriptCompiler.TryCompile(new Script { Source = raw, Language = ScriptLanguage.JavaScript }, out var err))
                {
                    errors.Add($"节点 \"{nodeId}\" 参数 \"{prop.Key}\" 的 JS 表达式无法编译：{err!.Message}");
                }
            }
        }
    }
}

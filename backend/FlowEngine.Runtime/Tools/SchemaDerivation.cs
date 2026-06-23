using System.Text.RegularExpressions;
using System.Text.Json.Nodes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;

namespace FlowEngine.Runtime.Tools;

/// <summary>
/// 从 ParameterDefinition 推导 JSON Schema。
/// </summary>
public static partial class SchemaDerivation
{
    private static readonly Regex AiParamPattern = AiParamRegex();

    /// <summary>
    /// 从参数定义列表推导 JSON Schema。
    /// </summary>
    public static JsonObject? DeriveSchema(IReadOnlyList<ParameterDefinition>? parameters)
    {
        if (parameters is null || parameters.Count == 0)
        {
            return null;
        }

        var properties = new JsonObject();
        var required = new JsonArray();
        var hasAiParam = false;

        foreach (var param in parameters)
        {
            if (HasAiParamPlaceholder(param.Description) || HasAiParamPlaceholder(param.DisplayName))
            {
                hasAiParam = true;
            }

            var prop = BuildPropertySchema(param);
            properties[param.Name] = prop;

            if (param.Required)
            {
                required.Add(param.Name);
            }
        }

        var schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties
        };

        if (required.Count > 0)
        {
            schema["required"] = required;
        }

        if (hasAiParam)
        {
            schema["aiParamStructured"] = true;
        }

        return schema;
    }

    /// <summary>
    /// 解析 {{ai_param:描述}} 占位符，转为结构化参数描述。
    /// </summary>
    public static string? ResolveAiParamDescription(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        return AiParamPattern.Replace(text, match =>
        {
            var description = match.Groups[1].Value.Trim();
            return description;
        });
    }

    /// <summary>
    /// 检测文本中是否包含 {{ai_param:...}} 占位符。
    /// </summary>
    public static bool HasAiParamPlaceholder(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        return AiParamPattern.IsMatch(text);
    }

    private static JsonObject BuildPropertySchema(ParameterDefinition param)
    {
        var prop = new JsonObject
        {
            ["type"] = MapParameterType(param.Type),
            ["description"] = ResolveAiParamDescription(param.Description)
                ?? ResolveAiParamDescription(param.DisplayName)
                ?? param.DisplayName
        };

        if (param.Options.Count > 0)
        {
            var enumArray = new JsonArray();
            foreach (var option in param.Options)
            {
                enumArray.Add(JsonValue.Create(option.Value.ToString()));
            }
            prop["enum"] = enumArray;
        }

        if (param.Type == ParameterType.Array && param.ItemDefinition is not null)
        {
            prop["items"] = BuildPropertySchema(param.ItemDefinition);
        }

        if (param.Type == ParameterType.Json && param.Fields.Count > 0)
        {
            var innerProperties = new JsonObject();
            foreach (var field in param.Fields)
            {
                innerProperties[field.Name] = BuildPropertySchema(field);
            }
            prop["properties"] = innerProperties;
        }

        return prop;
    }

    private static string MapParameterType(ParameterType type) => type switch
    {
        ParameterType.String => "string",
        ParameterType.Number => "number",
        ParameterType.Boolean => "boolean",
        ParameterType.Json => "object",
        ParameterType.Array => "array",
        ParameterType.Code => "string",
        ParameterType.Credential => "string",
        ParameterType.Script => "string",
        _ => "string"
    };

    [GeneratedRegex(@"\{\{ai_param:(.+?)\}\}", RegexOptions.Compiled)]
    private static partial Regex AiParamRegex();
}

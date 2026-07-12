using System.Text.Json;
using System.Text.Json.Nodes;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;

namespace FlowEngine.Core.Ai;

/// <summary>
/// 将 <see cref="INodeType"/> 和 <see cref="NodeTypeDescriptor"/> 适配为 AI-native 节点定义。
/// </summary>
public static class NodeDefinitionAdapter
{
    private static readonly HashSet<ParameterType> ExpressionTypes =
    [
        ParameterType.String,
        ParameterType.Json,
        ParameterType.Code,
        ParameterType.Script,
    ];

    private static readonly HashSet<ParameterType> SensitiveTypes =
    [
        ParameterType.Credential,
        ParameterType.File,
    ];

        private static readonly string[] SensitiveNamePatterns =
        ["secret", "token", "password", "apikey", "api_key", "api-key"];

    /// <summary>
    /// 将节点类型适配为 AI 节点定义。
    /// </summary>
    /// <param name="nodeType">节点类型实例。</param>
    /// <param name="descriptor">节点类型描述。</param>
    /// <returns>AI 节点定义。</returns>
    public static AiNodeDefinition ToAiDefinition(INodeType nodeType, NodeTypeDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(nodeType);
        ArgumentNullException.ThrowIfNull(descriptor);

        var providerOverride = nodeType as IAiDefinitionProvider;
        AiNodeDefinition? overrideDef = providerOverride?.GetAiDefinition(descriptor);

        // 覆盖优先级：IAiDefinitionProvider.GetAiDefinition() > 自动推导（设计 §3.4）。
        // 节点显式提供的字段优先采用，缺失时回退到从节点类型/描述符自动推导。
        var derivedIsTrigger = nodeType.Category.Equals("Trigger", StringComparison.OrdinalIgnoreCase)
                                || nodeType.DefaultIsEntry;

        var def = new AiNodeDefinition
        {
            Name = nodeType.TypeName,
            DisplayName = HasValue(overrideDef?.DisplayName) ? overrideDef!.DisplayName : nodeType.DisplayName,
            Category = HasValue(overrideDef?.Category) ? overrideDef!.Category : nodeType.Category,
            Description = GetDescription(nodeType, descriptor, overrideDef),
            IsTrigger = overrideDef is not null ? overrideDef.IsTrigger : derivedIsTrigger,
            Tags = GetTags(nodeType, overrideDef),
            InputSchema = overrideDef?.InputSchema ?? BuildInputSchema(descriptor),
            OutputSchema = BuildOutputSchema(descriptor, overrideDef),
            Ports = overrideDef is { Ports: { Count: > 0 } } ? overrideDef.Ports : BuildPorts(descriptor),
            Examples = overrideDef?.Examples ?? [],
        };

        return def;
    }

    /// <summary>
    /// 从 <see cref="AiNodeDefinition"/> 创建摘要。
    /// </summary>
    public static AiNodeSummary ToSummary(AiNodeDefinition def)
    {
        ArgumentNullException.ThrowIfNull(def);

        return new AiNodeSummary
        {
            Name = def.Name,
            DisplayName = def.DisplayName,
            Description = def.Description,
            Category = def.Category,
            Tags = def.Tags,
            IsTrigger = def.IsTrigger,
        };
    }

    /// <summary>
    /// 将 <see cref="ParameterType"/> 转换为 JSON Schema 类型节点。
    /// </summary>
    public static JsonNode ConvertParameterType(ParameterDefinition p)
    {
        ArgumentNullException.ThrowIfNull(p);

        if (p.Options.Count > 0)
        {
            var enumArr = new JsonArray();
            foreach (var opt in p.Options)
            {
                enumArr.Add(JsonValue.Create(opt.Value?.ToString()));
            }

            return new JsonObject
            {
                ["type"] = "string",
                ["enum"] = enumArr,
            };
        }

        return p.Type switch
        {
            ParameterType.String => new JsonObject { ["type"] = "string" },
            ParameterType.Number => new JsonObject { ["type"] = "number" },
            ParameterType.Boolean => new JsonObject { ["type"] = "boolean" },
            ParameterType.Json => BuildJsonSchema(p),
            ParameterType.Array => BuildArraySchema(p),
            ParameterType.Code or ParameterType.Script or ParameterType.Credential
                or ParameterType.Resource or ParameterType.File => new JsonObject { ["type"] = "string" },
            _ => new JsonObject { ["type"] = "string" },
        };
    }

    private static string GetDescription(
        INodeType nodeType,
        NodeTypeDescriptor descriptor,
        AiNodeDefinition? overrideDef)
    {
        if (overrideDef is not null && !string.IsNullOrEmpty(overrideDef.Description))
        {
            return overrideDef.Description;
        }

        // Use DisplayName as default description; parameter descriptions describe individual
        // parameters, not the node's purpose — using them as the node description misleads AI.
        return $"{nodeType.DisplayName} 节点";
    }

    private static List<string> GetTags(INodeType nodeType, AiNodeDefinition? overrideDef)
    {
        if (overrideDef is not null && overrideDef.Tags.Count > 0)
        {
            return overrideDef.Tags;
        }

        var categoryTag = nodeType.Category?.ToLowerInvariant();
        if (!string.IsNullOrEmpty(categoryTag))
        {
            return [categoryTag];
        }

        return [];
    }

    private static JsonNode BuildInputSchema(NodeTypeDescriptor descriptor)
    {
        var schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject(),
            ["required"] = new JsonArray(),
        };

        var properties = (JsonObject)schema["properties"]!;
        var required = (JsonArray)schema["required"]!;

        foreach (var p in descriptor.Parameters)
        {
            var propSchema = ConvertParameterType(p);

            if (!string.IsNullOrEmpty(p.Description))
            {
                propSchema["description"] = p.Description;
            }

            if (IsExpressionType(p.Type))
            {
                propSchema["supportsExpression"] = true;
            }

            if (p.DefaultValue is not null && !IsSensitive(p))
            {
                try
                {
                    var defaultNode = JsonSerializer.SerializeToNode(p.DefaultValue, JsonDefaults.Options);
                    if (defaultNode is not null)
                    {
                        propSchema["default"] = defaultNode;
                    }
                }
                catch
                {
                    // Ignore serialization failures for default values
                }
            }

            if (p.Required)
            {
                required.Add(p.Name);
            }

            properties[p.Name] = propSchema;
        }

        return schema;
    }

    private static JsonNode? BuildOutputSchema(
        NodeTypeDescriptor descriptor,
        AiNodeDefinition? overrideDef)
    {
        if (overrideDef?.OutputSchema is not null)
        {
            return overrideDef.OutputSchema;
        }

        var outputPort = descriptor.Ports.FirstOrDefault(p => p.Direction == PortDirection.Output);
        if (outputPort?.OutputSchema is not null)
        {
            return ConvertDataSchema(outputPort.OutputSchema);
        }

        return null;
    }

    private static List<AiPortSchema> BuildPorts(NodeTypeDescriptor descriptor)
    {
        return descriptor.Ports.Select(p => new AiPortSchema
        {
            Name = p.Name,
            Direction = p.Direction.ToString(),
            Description = p.DisplayName,
        }).ToList();
    }

    private static JsonNode BuildJsonSchema(ParameterDefinition p)
    {
        if (p.Fields.Count > 0)
        {
            var properties = new JsonObject();
            foreach (var field in p.Fields)
            {
                properties[field.Name] = ConvertParameterType(field);
            }

            return new JsonObject
            {
                ["type"] = "object",
                ["properties"] = properties,
            };
        }

        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject(),
        };
    }

    private static JsonNode BuildArraySchema(ParameterDefinition p)
    {
        JsonNode itemsSchema;

        if (p.ItemDefinition is not null)
        {
            itemsSchema = ConvertParameterType(p.ItemDefinition);

            if (p.ItemDefinition.Fields.Count > 0)
            {
                var properties = new JsonObject();
                foreach (var field in p.ItemDefinition.Fields)
                {
                    properties[field.Name] = ConvertParameterType(field);
                }

                itemsSchema = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = properties,
                };
            }
        }
        else if (p.Fields.Count > 0)
        {
            var properties = new JsonObject();
            foreach (var field in p.Fields)
            {
                properties[field.Name] = ConvertParameterType(field);
            }

            itemsSchema = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = properties,
            };
        }
        else
        {
            itemsSchema = new JsonObject();
        }

        return new JsonObject
        {
            ["type"] = "array",
            ["items"] = itemsSchema,
        };
    }

    private static JsonNode ConvertDataSchema(DataSchema schema)
    {
        var node = new JsonObject();

        if (!string.IsNullOrEmpty(schema.Type))
        {
            node["type"] = schema.Type;
        }

        if (schema.Properties.Count > 0)
        {
            var properties = new JsonObject();
            foreach (var kvp in schema.Properties)
            {
                properties[kvp.Key] = ConvertDataSchema(kvp.Value);
            }

            node["properties"] = properties;
        }

        if (schema.Required.Count > 0)
        {
            var requiredArr = new JsonArray();
            foreach (var r in schema.Required)
            {
                requiredArr.Add(r);
            }

            node["required"] = requiredArr;
        }

        if (schema.Items is not null)
        {
            node["items"] = ConvertDataSchema(schema.Items);
        }

        if (!string.IsNullOrEmpty(schema.Description))
        {
            node["description"] = schema.Description;
        }

        return node;
    }

    private static bool IsExpressionType(ParameterType type)
    {
        return ExpressionTypes.Contains(type);
    }

    private static bool HasValue(string? value)
    {
        return !string.IsNullOrEmpty(value);
    }

    private static bool IsSensitive(ParameterDefinition p)
    {
        if (SensitiveTypes.Contains(p.Type))
        {
            return true;
        }

        if (p.Name is null)
        {
            return false;
        }

        foreach (var pattern in SensitiveNamePatterns)
        {
            if (p.Name.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}

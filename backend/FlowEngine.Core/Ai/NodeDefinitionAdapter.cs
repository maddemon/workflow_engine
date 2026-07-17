using System.Text.Json;
using System.Text.Json.Nodes;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Scripting;

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

    private static readonly Dictionary<string, Dictionary<string, string[]>> AuthFieldMappings = new()
    {
        ["authentication"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["BearerToken"] = ["accessToken", "token"],
            ["QueryParameter"] = ["accessToken", "token", "apiKey"],
            ["ApiKey"] = ["apiKey"],
            ["BasicAuth"] = ["username", "password"],
        },
    };

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

        // 覆盖优先级：INodeType.GetAiDefinition() > 自动推导（设计 §3.4）。
        AiNodeDefinition? overrideDef = nodeType.GetAiDefinition(descriptor);
        // 节点显式提供的字段优先采用，缺失时回退到从节点类型/描述符自动推导。
        // 注意：isTrigger 仅以节点类别是否为 Trigger 为准。DefaultIsEntry=true 的非触发器节点
        // （如 llm）不能作为工作流入口，误标会误导 AI 将其当作触发器（task-013 P4）。
        var derivedIsTrigger = nodeType.Category.Equals("Trigger", StringComparison.OrdinalIgnoreCase);

        var def = new AiNodeDefinition
        {
            Name = nodeType.TypeName,
            DisplayName = HasValue(overrideDef?.DisplayName) ? overrideDef!.DisplayName : nodeType.DisplayName,
            Category = HasValue(overrideDef?.Category) ? overrideDef!.Category : nodeType.Category,
            Description = GetDescription(nodeType, descriptor, overrideDef),
            IsTrigger = overrideDef is not null ? overrideDef.IsTrigger : derivedIsTrigger,
            Tags = GetTags(nodeType, overrideDef),
            InputSchema = overrideDef?.InputSchema ?? BuildInputSchema(descriptor),
            OutputSchema = AddOutputDescription(BuildOutputSchema(descriptor, overrideDef)),
            Ports = overrideDef is { Ports: { Count: > 0 } } ? overrideDef.Ports : BuildPorts(descriptor),
            Examples = overrideDef?.Examples ?? [],
            ExpressionLanguage = "javascript",
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

    internal static JsonNode BuildInputSchema(NodeTypeDescriptor descriptor)
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
                propSchema["expressionLanguage"] = JsonValue.Create("javascript");

                var examples = GetExpressionExamples(p.Name);
                if (examples is not null) propSchema["examples"] = examples;

                var anti = GetAntiPatterns(p.Name);
                if (anti is not null) propSchema["antiPatterns"] = anti;
            }

            if (p.DefaultValue is not null && !IsSensitive(p))
            {
                try
                {
                    // Script/Code 类型在 AI 视角下是字符串（schema 中 type:string），
                    // 直接序列化默认值会得到 {"source":""} 对象，与 schema 矛盾，
                    // 因此只暴露 Source 字符串（task-013 P7）。
                    if ((p.Type == ParameterType.Script || p.Type == ParameterType.Code)
                        && p.DefaultValue is Script scriptDefault)
                    {
                        if (!string.IsNullOrEmpty(scriptDefault.Source))
                        {
                            propSchema["default"] = JsonValue.Create(scriptDefault.Source);
                        }
                    }
                    else
                    {
                        var defaultNode = JsonSerializer.SerializeToNode(p.DefaultValue, JsonDefaults.Options);
                        if (defaultNode is not null)
                        {
                            propSchema["default"] = defaultNode;
                        }
                    }
                }
                catch
                {
                    // Ignore serialization failures for default values
                }
            }

            // 与校验逻辑（P5a）保持一致：带默认值的必填参数本质是可选的，
            // 不应出现在 required 中，避免 AI 收到矛盾的「必填」提示。
            if (p.Required && p.DefaultValue is null)
            {
                required.Add(p.Name);
            }

            // 认证模式字段映射：让 AI 知道每种认证模式需要哪些凭据字段。
            if (p.Options.Count > 0 && AuthFieldMappings.TryGetValue(p.Name, out var modeMap))
            {
                var mapping = new JsonObject();
                foreach (var (mode, fields) in modeMap)
                {
                    var arr = new JsonArray();
                    foreach (var f in fields) arr.Add(f);
                    mapping[mode] = arr;
                }

                propSchema["credentialFieldMapping"] = mapping;
            }

            // 凭据类型：让 AI 知道要传对应类型的凭据 ID，不要填占位符。
            if (p.Type == ParameterType.Credential && !string.IsNullOrEmpty(p.CredentialType))
            {
                var typeList = p.CredentialType.Contains(',')
                    ? p.CredentialType.Split(',').Select(t => t.Trim()).ToList()
                    : [p.CredentialType];
                var typeDisplay = typeList.Count == 1
                    ? $"'{typeList[0]}'"
                    : $"[{string.Join(", ", typeList.Select(t => $"'{t}'"))}]";
                propSchema["credentialType"] = typeList.Count == 1
                    ? JsonValue.Create(typeList[0])
                    : new JsonArray(typeList.Select(t => JsonValue.Create(t)).ToArray());
                var desc = string.IsNullOrEmpty(p.Description)
                    ? $"必须是类型为 {typeDisplay} 的凭据 ID，不要填占位符。"
                    : p.Description + $" 必须是类型为 {typeDisplay} 的凭据 ID，不要填占位符。";
                propSchema["description"] = JsonValue.Create(desc);
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
            Type = p.Type.ToString(),
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

    private static JsonArray? GetExpressionExamples(string? paramName) => paramName?.ToLowerInvariant() switch
    {
        "url" => new JsonArray
        {
            "'https://api.example.com/items'",
            "'https://api.example.com/items/' + $json.id",
            "'https://api.example.com/items?page=' + $json.page + '&size=100'",
            "'https://oapi.dingtalk.com/topapi/v2/user/list?access_token=' + $json.body.access_token",
        },
        "bodyexpression" or "body_expression" or "body" => new JsonArray
        {
            "return { name: $json.name, count: $json.count };",
            "return { items: $input.all().map(i => i.data) };",
        },
        "headersexpression" or "headers_expression" or "headers" => new JsonArray
        {
            "return { 'Authorization': 'Bearer ' + $json.token };",
        },
        "successwhen" => new JsonArray
        {
            "$json.errcode == 0",
            "$json.status == 'ok'",
        },
        _ => null,
    };

    private static JsonArray? GetAntiPatterns(string? paramName) => paramName?.ToLowerInvariant() switch
    {
        "url" => new JsonArray
        {
            JsonNode.Parse("""{"wrong":"https://x?t={{$json.token}}","why":"{{ }} 是 n8n mustache 模板，本引擎不支持；裸写会被 JS 解析为 '//' 注释导致编译报错，带引号则静默通过并错把 token 原样发出。务必用 JS 拼接","right":"'https://x?t=' + $json.body.token"}"""),
        },
        "bodyexpression" or "body_expression" or "body" => new JsonArray
        {
            JsonNode.Parse("""{"wrong":"return { token: {{$json.token}} }","why":"{{ }} 不是本引擎语法","right":"return { token: $json.token }"}"""),
        },
        "successwhen" => new JsonArray
        {
            JsonNode.Parse("""{"wrong":"{{$json.errcode}} == 0","why":"{{ }} 不是本引擎语法","right":"$json.errcode == 0"}"""),
        },
        _ => null,
    };

    private static JsonNode? AddOutputDescription(JsonNode? outputSchema)
    {
        if (outputSchema is JsonObject outObj && !outObj.ContainsKey("description"))
        {
            outObj["description"] = JsonValue.Create(
                "节点输出 data 的结构。例如 HTTP 节点响应被包成 { statusCode, headers, body }，" +
                "下游用 $input.first().body.x 取业务字段，而不是 $input.first().x 或 .result。");
        }

        return outputSchema;
    }
}

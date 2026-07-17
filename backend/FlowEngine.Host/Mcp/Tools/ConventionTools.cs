using System.ComponentModel;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;

namespace FlowEngine.Host.Mcp.Tools;

[McpServerToolType]
public sealed class ConventionTools
{
    [McpServerTool(Name = "get_conventions")]
    [Description("返回 Flow Engine 的全局约定，尤其是表达式语言。AI 在组装工作流前应优先阅读，以消解 n8n 等其它工具的心智模型差异。")]
    public JsonNode GetConventions()
    {
        return new JsonObject
        {
            ["expressionLanguage"] = "javascript",
            ["summary"] =
                "本引擎的 Script/Expression 参数是 JavaScript 表达式（Jint），使用 $json（当前 item 数据）和 " +
                "$input（输入容器）。不支持 n8n 的 {{ }} mustache 模板，也不要使用其它模板语法。",
            ["rules"] = new JsonArray
            {
                "'https://api.com/path?token=' + $json.token（不要写 {{$json.token}}）",
                "引用上游输出用 $json；多 item/数组用 $input.all() / $input.first()",
                "HTTP 节点响应被包成 { statusCode, headers, body }，下游用 $input.first().body.x 取业务字段",
                "字符串拼接用 + 与单/双引号；禁止 {{ }} 模板",
                "端口名大小写敏感：使用 get_node_detail 返回的准确端口名（如 \"True\" 而非 \"true\"），连接端口不匹配会被拒绝",
                "连接须注意端口类型兼容性：get_node_detail 返回的 type 字段（Main/AgentTool/LLM/Memory）标识端口类型，不同类型节点不能直接连接",
            },
        };
    }
}
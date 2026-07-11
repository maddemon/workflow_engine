using System.Text;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;

namespace FlowEngine.Application.Workflows;

/// <summary>
/// AI 工作流生成的 Prompt 模板与节点类型序列化。
/// 系统 Prompt 固化 DSL 结构约束、可用节点清单与钉钉配方（Few-shot），
/// 降低 LLM 生成非法 DSL 的概率；纠错 Prompt 回传结构化错误清单。
/// </summary>
public static class PromptTemplates
{
    /// <summary>
    /// 构建系统 Prompt。
    /// </summary>
    /// <param name="registry">节点注册中心，用于生成实时节点类型清单。</param>
    /// <returns>系统 Prompt 文本。</returns>
    public static string BuildSystemPrompt(INodeRegistry registry)
    {
        var sb = new StringBuilder();

        sb.AppendLine("你是 Flow Engine 工作流编排助手。根据用户自然语言描述生成合法的工作流 JSON。");
        sb.AppendLine();
        sb.AppendLine("## DSL 结构");
        sb.AppendLine("- 顶层字段：name（字符串，必填）、projectId?（可选 Guid）、nodes[]（非空）、connections[]（数组）、styleSettings?（可选）。");
        sb.AppendLine("- 节点：id（字符串，唯一）、typeName、name、parameters{}、ports[]、positionX、positionY、isEntry?（布尔，至少 1 个入口）。");
        sb.AppendLine("- 连接：id、sourceNodeId、sourcePortName（须为源节点的 Output 端口）、targetNodeId、targetPortName（须为目标节点的 Input 端口）。");
        sb.AppendLine();
        sb.AppendLine("## 约束");
        sb.AppendLine("1. 至少一个 isEntry=true 的入口节点。");
        sb.AppendLine("2. typeName 必须来自下方“可用节点类型”列表。");
        sb.AppendLine("3. 连接的 sourcePortName 必须是 Output 端口，targetPortName 必须是 Input 端口。");
        sb.AppendLine("4. 节点必填参数（标注 [必填]）必须提供值。");
        sb.AppendLine("5. 凭据类型参数传入凭据【名称】（字符串），不要传 Guid；运行时按名称解析。");
        sb.AppendLine("6. 表达式中可用 $json / $input / $credentials.<name>.<field> 等变量。");
        sb.AppendLine("7. 仅返回 JSON，不要 markdown 代码围栏包裹，不要任何解释文字。");
        sb.AppendLine();
        sb.AppendLine("## 可用节点类型");
        sb.AppendLine(SerializeNodeTypes(registry));
        sb.AppendLine();
        sb.AppendLine("## 钉钉同步配方（必须固化，避免错误取 token 方案）");
        sb.AppendLine("1. 凭据：type=oauth2，引擎按“钉钉策略”GET gettoken?appkey&appsecret 自动缓存/刷新；");
        sb.AppendLine("   下游在 URL query 中引用 $credentials.<name>.accessToken，【不要】自建 dingtalk 专用节点。");
        sb.AppendLine("2. 拉取：用 paginate 节点，url 含 ?access_token=$credentials.<name>.accessToken，");
        sb.AppendLine("   body {dept_id, cursor, size}，itemsPath=result.list，nextCursorPath=result.next_cursor，");
        sb.AppendLine("   terminateWhen=$nextCursor == ''，cursorType=string。");
        sb.AppendLine("3. 映射：轻量字段重命名优先用 set 节点（字段值支持表达式）；复杂映射用 script。");
        sb.AppendLine("4. 写库：用 dbUpsert，connection=$credentials.<db>.connectionString，mode=upsert，keyColumns 设主键。");
        sb.AppendLine();
        sb.AppendLine("## 示例（钉钉部门用户 → 数据库）");
        sb.AppendLine("""
        {
          "name": "钉钉员工同步",
          "nodes": [
            { "id": "trigger", "typeName": "manualTrigger", "isEntry": true, "parameters": {} },
            { "id": "fetch", "typeName": "paginate", "parameters": {
                "url": "https://oapi.dingtalk.com/topapi/v2/user/list?access_token=$credentials.dingtalk.accessToken",
                "method": "POST",
                "body": { "dept_id": 1, "cursor": 0, "size": 100 },
                "itemsPath": "result.list", "nextCursorPath": "result.next_cursor",
                "terminateWhen": "$nextCursor == ''", "cursorType": "string"
            } },
            { "id": "map", "typeName": "set", "parameters": {
                "fields": [ { "name": "userId", "value": "$json.userid" }, { "name": "name", "value": "$json.name" } ]
            } },
            { "id": "save", "typeName": "dbUpsert", "parameters": {
                "connection": "$credentials.db.connectionString", "table": "employees", "mode": "upsert",
                "keyColumns": ["userId"]
            } }
          ],
          "connections": [
            { "sourceNodeId": "trigger", "sourcePortName": "out", "targetNodeId": "fetch", "targetPortName": "in" },
            { "sourceNodeId": "fetch", "sourcePortName": "out", "targetNodeId": "map", "targetPortName": "in" },
            { "sourceNodeId": "map", "sourcePortName": "out", "targetNodeId": "save", "targetPortName": "in" }
          ]
        }
        """);

        return sb.ToString();
    }

    /// <summary>
    /// 构建纠错消息，将校验错误回传给 LLM 以触发修正。
    /// </summary>
    /// <param name="errors">结构化校验错误清单。</param>
    /// <returns>纠错 Prompt 文本。</returns>
    public static string BuildCorrectionMessage(IReadOnlyList<string> errors)
    {
        var sb = new StringBuilder();
        sb.AppendLine("你上次生成的工作流未通过校验，存在以下错误：");
        for (var i = 0; i < errors.Count; i++)
        {
            sb.AppendLine($"{i + 1}. {errors[i]}");
        }
        sb.AppendLine("请修正后重新输出【完整】的工作流 JSON（不要 markdown 包裹，不要解释）。");
        return sb.ToString();
    }

    /// <summary>
    /// 将节点类型清单序列化为紧凑文本。
    /// </summary>
    private static string SerializeNodeTypes(INodeRegistry registry)
    {
        var sb = new StringBuilder();
        foreach (var d in registry.GetDescriptors().OrderBy(d => d.TypeName, StringComparer.Ordinal))
        {
            var required = d.Parameters.Where(p => p.Required).Select(p => $"{p.Name}[必填]").ToList();
            var optional = d.Parameters.Where(p => !p.Required).Select(p => p.Name).ToList();
            var ports = string.Join(", ", d.Ports.Select(p => $"{p.Name}:{p.Direction}"));

            sb.Append('-');
            sb.Append($" {d.TypeName}（{d.DisplayName}，分类 {d.Category}）");
            sb.Append(" 参数: ");
            sb.Append(required.Count > 0 ? string.Join(", ", required) : "无必填");
            if (optional.Count > 0)
            {
                sb.Append("；可选: ").Append(string.Join(", ", optional));
            }
            sb.Append("；端口: ").Append(string.IsNullOrEmpty(ports) ? "无" : ports);
            sb.AppendLine();
        }

        return sb.ToString();
    }
}

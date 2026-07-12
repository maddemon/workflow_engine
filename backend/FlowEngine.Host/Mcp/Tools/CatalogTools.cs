using System.ComponentModel;
using FlowEngine.Application.Workflows;
using FlowEngine.Core.Ai;
using ModelContextProtocol.Server;

namespace FlowEngine.Host.Mcp.Tools;

/// <summary>
/// Catalog MCP 工具，供 AI 发现与查看 Flow Engine 节点目录。
/// </summary>
[McpServerToolType]
public sealed class CatalogTools(CatalogService catalogService)
{
    /// <summary>
    /// 列出 Flow Engine 节点目录。
    /// </summary>
    /// <param name="category">节点分类，如 core、trigger、integration 等；不传入则返回全部。</param>
    /// <returns>节点摘要列表。</returns>
    [McpServerTool(Name = "list_node_catalog")]
    [Description("列出 Flow Engine 节点目录。AI 可传入可选的 category 过滤指定分类；不传入则返回全部节点摘要。")]
    public IReadOnlyList<AiNodeSummary> ListNodeCatalog(
        [Description("节点分类，如 core、trigger、integration 等。")] string? category = null)
    {
        var all = catalogService.ListAll();

        if (string.IsNullOrWhiteSpace(category))
        {
            return all;
        }

        return all
            .Where(node => node.Category.Equals(category, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// 获取指定节点类型的完整定义。
    /// </summary>
    /// <param name="name">节点类型名，如 httpRequest。</param>
    /// <returns>节点完整定义；未找到时返回结构化错误信息。</returns>
    [McpServerTool(Name = "get_node_detail")]
    [Description("获取指定节点类型的完整定义，包括输入 schema、输出 schema、端口和示例。用于 AI 在组装工作流前确认节点契约。")]
    public object GetNodeDetail(
        [Description("节点类型名，如 httpRequest。")] string name)
    {
        var definition = catalogService.GetByName(name);

        if (definition is null)
        {
            return new
            {
                success = false,
                errorCode = "NodeNotFound",
                message = $"节点 '{name}' 不存在",
            };
        }

        return definition;
    }
}

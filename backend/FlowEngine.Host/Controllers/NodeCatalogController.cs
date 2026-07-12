using FlowEngine.Application.Workflows;
using FlowEngine.Core.Ai;
using Microsoft.AspNetCore.Mvc;

namespace FlowEngine.Host.Controllers;

/// <summary>
/// AI 节点目录 API，供外部 AI 发现节点。
/// </summary>
[ApiController]
[Route("api/v1/node-catalog")]
public class NodeCatalogController(CatalogService catalogService) : ControllerBase
{
    /// <summary>
    /// 获取所有节点摘要列表。
    /// </summary>
    /// <returns>节点摘要列表。</returns>
    [HttpGet]
    public ActionResult<IReadOnlyList<AiNodeSummary>> GetAll()
    {
        var nodes = catalogService.ListAll();
        return Ok(nodes);
    }

    /// <summary>
    /// 按类型名获取节点完整定义。
    /// </summary>
    /// <param name="name">节点类型名。</param>
    /// <returns>节点完整定义。</returns>
    [HttpGet("{name}")]
    public ActionResult<AiNodeDefinition> GetByName(string name)
    {
        var node = catalogService.GetByName(name);
        if (node is null)
        {
            return NotFound(new
            {
                success = false,
                errorCode = "NodeNotFound",
                message = $"节点 '{name}' 不存在",
            });
        }

        return Ok(node);
    }
}

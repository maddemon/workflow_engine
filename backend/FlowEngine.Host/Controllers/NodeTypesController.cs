using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using Microsoft.AspNetCore.Mvc;

namespace FlowEngine.Host.Controllers;

/// <summary>
/// 节点类型 API。
/// </summary>
[ApiController]
[Route("api/v1/node-types")]
public class NodeTypesController(INodeRegistry nodeRegistry) : ControllerBase
{
    /// <summary>
    /// 获取所有节点类型描述，支持按分类过滤。
    /// </summary>
    /// <param name="category">节点分类过滤条件。</param>
    /// <returns>节点类型描述列表。</returns>
    [HttpGet]
    public ActionResult<IReadOnlyCollection<NodeTypeDescriptor>> GetAll(string? category = null)
    {
        ArgumentNullException.ThrowIfNull(nodeRegistry);
        var descriptors = nodeRegistry.GetDescriptors();

        if (!string.IsNullOrWhiteSpace(category))
        {
            descriptors = descriptors
                .Where(d => d.Category.Equals(category, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        return Ok(descriptors);
    }
}

using System.Text.Json;
using System.Text.Json.Nodes;
using FlowEngine.Application.Audit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlowEngine.Host.Controllers;

/// <summary>
/// 审计日志查询 API。
/// </summary>
[ApiController]
[Route("api/v1/audit-events")]
[Authorize(Roles = "Admin")]
public class AuditEventsController(IAuditLogReader reader) : ControllerBase
{
    /// <summary>
    /// 查询审计事件，支持按类型、时间、资源过滤与分页。
    /// </summary>
    [HttpGet]
    public async Task<ActionResult> Query(AuditQueryParameters parameters, CancellationToken cancellationToken = default)
    {
        var result = await reader.QueryAsync(parameters, cancellationToken).ConfigureAwait(false);

        // 使用内置 JsonNode.Parse 将审计事件文档直接转换为节点树，避免手写递归转换，
        // 输出语义（字段名/类型/结构）与原始 JSON 完全一致。
        var events = result.Events.Select(doc =>
        {
            using (doc)
            {
                return (object)JsonNode.Parse(doc.RootElement.GetRawText())!;
            }
        }).ToList();

        return Ok(new
        {
            total = result.Total,
            offset = parameters.Offset,
            limit = parameters.Limit,
            events,
        });
    }
}

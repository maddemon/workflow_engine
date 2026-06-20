using System.Text.Json;
using FlowEngine.Infrastructure.Audit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlowEngine.Host.Controllers;

/// <summary>
/// 审计日志查询 API。
/// </summary>
[ApiController]
[Route("api/v1/audit-events")]
[Authorize]
public class AuditEventsController(AuditLogReader reader) : ControllerBase
{
    /// <summary>
    /// 查询审计事件，支持按类型、时间、资源过滤与分页。
    /// </summary>
    [HttpGet]
    public async Task<ActionResult> Query(
        [FromQuery] string? eventType,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? resourceType,
        [FromQuery] Guid? resourceId,
        [FromQuery] int offset = 0,
        [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var parameters = new AuditQueryParameters
        {
            EventType = eventType,
            From = from,
            To = to,
            ResourceType = resourceType,
            ResourceId = resourceId,
            Offset = Math.Max(0, offset),
            Limit = Math.Clamp(limit, 1, 200),
        };

        var result = await reader.QueryAsync(parameters, cancellationToken)
            .ConfigureAwait(false);

        var events = result.Events.Select(doc =>
        {
            using (doc)
            {
                return JsonNodeFromElement(doc.RootElement);
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

    private static object JsonNodeFromElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject()
                .ToDictionary(p => p.Name, p => JsonNodeFromElement(p.Value)),
            JsonValueKind.Array => element.EnumerateArray()
                .Select(JsonNodeFromElement).ToList(),
            JsonValueKind.String => element.GetString()!,
            JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null!,
        };
    }
}

using System.ComponentModel;
using FlowEngine.Application.Credentials;
using ModelContextProtocol.Server;

namespace FlowEngine.Host.Mcp.Tools;

/// <summary>
/// 凭据 MCP 工具，供 AI 查询系统中可用的凭据。
/// </summary>
[McpServerToolType]
public sealed class CredentialTools(CredentialService credentialService)
{
    /// <summary>
    /// 列出系统中所有可用凭据的摘要信息（不含敏感值）。
    /// AI 可通过此工具了解系统中有哪些凭据，以便在工作流中正确引用。
    /// </summary>
    [McpServerTool(Name = "list_credentials")]
    [Description("列出系统中所有可用凭据的摘要信息（不含敏感值）。返回凭据名称、类型、ID 等，供 AI 在工作流节点中引用。")]
    public async Task<object> ListCredentials(
        [Description("项目 ID（可选，不传则返回所有凭据）")] string? projectId = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            Guid? projectGuid = null;
            if (!string.IsNullOrWhiteSpace(projectId) && Guid.TryParse(projectId, out var pid))
            {
                projectGuid = pid;
            }

            var result = await credentialService.GetAllAsync(projectGuid, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return new
            {
                credentials = result.Items.Select(c => new
                {
                    id = c.Id,
                    name = c.Name,
                    type = c.Type,
                }).ToList(),
                total = result.TotalCount,
            };
        }
        catch (Exception ex)
        {
            return new McpToolError(
                "ListFailed",
                $"列出凭据失败: {ex.Message}",
                CanAutoFix: false);
        }
    }
}

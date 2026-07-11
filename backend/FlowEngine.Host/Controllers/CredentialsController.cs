using FlowEngine.Application.Credentials;
using FlowEngine.Application.Dtos;
using FlowEngine.Core.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlowEngine.Host.Controllers;

/// <summary>
/// 凭据 CRUD API。
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/credentials")]
public class CredentialsController(CredentialService credentialService) : ControllerBase
{
    /// <summary>
    /// 获取所有凭据摘要列表。
    /// </summary>
    [HttpGet]
    [AuthorizePermission(Scope.Credential, Operation.Read)]
    public async Task<ActionResult<IReadOnlyCollection<CredentialDto>>> GetAll(
        [FromQuery] Guid? projectId = null,
        CancellationToken cancellationToken = default)
    {
        var credentials = await credentialService.GetAllAsync(projectId, cancellationToken).ConfigureAwait(false);
        return Ok(credentials);
    }

    /// <summary>
    /// 按 ID 获取凭据摘要。
    /// </summary>
    [HttpGet("{id:guid}")]
    [AuthorizePermission(Scope.Credential, Operation.Read)]
    public async Task<ActionResult<CredentialDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var credential = await credentialService.GetAsync(id, cancellationToken).ConfigureAwait(false);
        return this.OkOrNotFound(credential);
    }

    /// <summary>
    /// 幂等创建或更新凭据。
    /// </summary>
    [HttpPost("ensure")]
    [AuthorizePermission(Scope.Credential, Operation.Write)]
    public async Task<ActionResult<CredentialDto>> Ensure(
        [FromBody] CreateCredentialDto dto,
        CancellationToken cancellationToken)
    {
        var (credential, created) = await credentialService.EnsureAsync(dto, cancellationToken).ConfigureAwait(false);
        if (created)
        {
            return CreatedAtAction(nameof(Get), new { id = credential.Id }, credential);
        }

        return Ok(credential);
    }

    /// <summary>
    /// 创建凭据。
    /// </summary>
    [HttpPost]
    [AuthorizePermission(Scope.Credential, Operation.Write)]
    public async Task<ActionResult<CredentialDto>> Create(
        [FromBody] CreateCredentialDto dto,
        CancellationToken cancellationToken)
    {
        var credential = await credentialService.CreateAsync(dto, cancellationToken).ConfigureAwait(false);
        return CreatedAtAction(nameof(Get), new { id = credential.Id }, credential);
    }

    /// <summary>
    /// 更新凭据。
    /// </summary>
    [HttpPut("{id:guid}")]
    [AuthorizePermission(Scope.Credential, Operation.Write)]
    public async Task<ActionResult<CredentialDto>> Update(
        Guid id,
        [FromBody] UpdateCredentialDto dto,
        CancellationToken cancellationToken)
    {
        var credential = await credentialService.UpdateAsync(id, dto, cancellationToken).ConfigureAwait(false);
        return this.OkOrNotFound(credential);
    }

    /// <summary>
    /// 删除凭据。
    /// </summary>
    [HttpDelete("{id:guid}")]
    [AuthorizePermission(Scope.Credential, Operation.Delete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var result = await credentialService.DeleteAsync(id, cancellationToken).ConfigureAwait(false);

        if (result.NotFound)
        {
            return NotFound();
        }

        if (result.ReferencedBy.Count > 0)
        {
            return Conflict(new
            {
                message = "凭据被工作流引用，无法删除。",
                referencedBy = result.ReferencedBy
            });
        }

        return NoContent();
    }
}

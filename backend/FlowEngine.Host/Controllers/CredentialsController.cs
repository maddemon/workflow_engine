using FlowEngine.Application.Credentials;
using FlowEngine.Application.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace FlowEngine.Host.Controllers;

/// <summary>
/// 凭据 CRUD API。
/// </summary>
[ApiController]
[Route("api/v1/credentials")]
public class CredentialsController(CredentialService credentialService) : ControllerBase
{
    /// <summary>
    /// 获取所有凭据摘要列表。
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<CredentialDto>>> GetAll(
        CancellationToken cancellationToken)
    {
        var credentials = await credentialService.GetAllAsync(cancellationToken).ConfigureAwait(false);
        return Ok(credentials);
    }

    /// <summary>
    /// 按 ID 获取凭据摘要。
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CredentialDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var credential = await credentialService.GetAsync(id, cancellationToken).ConfigureAwait(false);
        if (credential is null)
        {
            return NotFound();
        }

        return Ok(credential);
    }

    /// <summary>
    /// 创建凭据。
    /// </summary>
    [HttpPost]
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
    public async Task<ActionResult<CredentialDto>> Update(
        Guid id,
        [FromBody] UpdateCredentialDto dto,
        CancellationToken cancellationToken)
    {
        var credential = await credentialService.UpdateAsync(id, dto, cancellationToken).ConfigureAwait(false);
        if (credential is null)
        {
            return NotFound();
        }

        return Ok(credential);
    }

    /// <summary>
    /// 删除凭据。
    /// </summary>
    [HttpDelete("{id:guid}")]
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

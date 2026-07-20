using System.Net;
using System.Net.Http.Json;
using FlowEngine.Application.Dtos;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Authorization;
using FlowEngine.Core.Credentials;
using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Identity;
using FlowEngine.Host.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FlowEngine.Host.Tests.Controllers;

public class CredentialsControllerTests : HostIntegrationTestBase
{
    public CredentialsControllerTests(FlowEngineWebApplicationFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task GetAll_AuthenticatedAdmin_ReturnsOkWithCredentials()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = "credentials-getall@example.com";
        var client = await CreateAuthenticatedClientAsync(email, [RoleConstants.Admin], ct);
        var credential = await SeedCredentialAsync(email, ct);

        var response = await client.GetAsync("/api/v1/credentials", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<IReadOnlyCollection<CredentialDto>>(TestJsonOptions, ct);
        Assert.NotNull(result);
        Assert.Contains(result, c => c.Id == credential.Id);
    }

    [Fact]
    public async Task GetAll_ByProject_ReturnsOkWithCredentials()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = "credentials-getall-project@example.com";
        var client = await CreateAuthenticatedClientAsync(email, [RoleConstants.Admin], ct);
        var credential = await SeedCredentialAsync(email, ct);

        var response = await client.GetAsync($"/api/v1/credentials?projectId={credential.ProjectId}", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<IReadOnlyCollection<CredentialDto>>(TestJsonOptions, ct);
        Assert.NotNull(result);
        Assert.Contains(result, c => c.Id == credential.Id);
    }

    [Fact]
    public async Task GetTypes_ReturnsOk()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await CreateAuthenticatedClientAsync("credentials-types@example.com", [RoleConstants.Admin], ct);

        var response = await client.GetAsync("/api/v1/credentials/types", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<System.Text.Json.Nodes.JsonArray>(TestJsonOptions, ct);
        Assert.NotNull(result);
        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task Get_Existing_ReturnsOk()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = "credentials-get@example.com";
        var client = await CreateAuthenticatedClientAsync(email, [RoleConstants.Admin], ct);
        var credential = await SeedCredentialAsync(email, ct);

        var response = await client.GetAsync($"/api/v1/credentials/{credential.Id}", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<CredentialDto>(TestJsonOptions, ct);
        Assert.NotNull(result);
        Assert.Equal(credential.Id, result!.Id);
    }

    [Fact]
    public async Task Get_NotFound_Returns404()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await CreateAuthenticatedClientAsync("credentials-get-notfound@example.com", [RoleConstants.Admin], ct);

        var response = await client.GetAsync($"/api/v1/credentials/{Guid.NewGuid()}", ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Ensure_NewCredential_ReturnsCreated()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await CreateAuthenticatedClientAsync("credentials-ensure@example.com", [RoleConstants.Admin], ct);
        var dto = new CreateCredentialDto
        {
            Name = "Ensure Credential",
            Type = "apiKey",
            Fields = new Dictionary<string, string> { ["apiKey"] = "secret-key" },
        };

        var response = await client.PostAsJsonAsync("/api/v1/credentials/ensure", dto, ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<CredentialDto>(TestJsonOptions, ct);
        Assert.NotNull(result);
        Assert.Equal(dto.Name, result!.Name);
    }

    [Fact]
    public async Task Ensure_ExistingCredential_ReturnsOk()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = "credentials-ensure-existing@example.com";
        var client = await CreateAuthenticatedClientAsync(email, [RoleConstants.Admin], ct);
        var existing = await SeedCredentialAsync(email, ct);
        var dto = new CreateCredentialDto
        {
            Name = existing.Name,
            Type = existing.Type,
            ProjectId = existing.ProjectId,
            Fields = new Dictionary<string, string> { ["apiKey"] = "updated-key" },
        };

        var response = await client.PostAsJsonAsync("/api/v1/credentials/ensure", dto, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<CredentialDto>(TestJsonOptions, ct);
        Assert.NotNull(result);
        Assert.Equal(existing.Id, result!.Id);
    }

    [Fact]
    public async Task Create_ValidDto_ReturnsCreated()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await CreateAuthenticatedClientAsync("credentials-create@example.com", [RoleConstants.Admin], ct);
        var dto = new CreateCredentialDto
        {
            Name = "New Credential",
            Type = "apiKey",
            Fields = new Dictionary<string, string> { ["apiKey"] = "secret-key" },
        };

        var response = await client.PostAsJsonAsync("/api/v1/credentials", dto, ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<CredentialDto>(TestJsonOptions, ct);
        Assert.NotNull(result);
        Assert.Equal(dto.Name, result!.Name);
    }

    [Fact]
    public async Task Create_InvalidType_ReturnsBadRequest()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await CreateAuthenticatedClientAsync("credentials-create-invalid@example.com", [RoleConstants.Admin], ct);
        var dto = new CreateCredentialDto
        {
            Name = "New Credential",
            Type = "unknown",
            Fields = new Dictionary<string, string> { ["apiKey"] = "secret-key" },
        };

        var response = await client.PostAsJsonAsync("/api/v1/credentials", dto, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_Existing_ReturnsOk()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = "credentials-update@example.com";
        var client = await CreateAuthenticatedClientAsync(email, [RoleConstants.Admin], ct);
        var credential = await SeedCredentialAsync(email, ct);
        var dto = new UpdateCredentialDto
        {
            Name = "Updated Credential",
            Fields = new Dictionary<string, string> { ["apiKey"] = "updated-key" },
        };

        var response = await client.PutAsJsonAsync($"/api/v1/credentials/{credential.Id}", dto, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<CredentialDto>(TestJsonOptions, ct);
        Assert.NotNull(result);
        Assert.Equal(dto.Name, result!.Name);
    }

    [Fact]
    public async Task Update_NotFound_Returns404()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await CreateAuthenticatedClientAsync("credentials-update-notfound@example.com", [RoleConstants.Admin], ct);
        var dto = new UpdateCredentialDto
        {
            Name = "Updated Credential",
            Fields = new Dictionary<string, string> { ["apiKey"] = "updated-key" },
        };

        var response = await client.PutAsJsonAsync($"/api/v1/credentials/{Guid.NewGuid()}", dto, ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_Existing_ReturnsNoContent()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = "credentials-delete@example.com";
        var client = await CreateAuthenticatedClientAsync(email, [RoleConstants.Admin], ct);
        var credential = await SeedCredentialAsync(email, ct);

        var response = await client.DeleteAsync($"/api/v1/credentials/{credential.Id}", ct);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Delete_NotFound_Returns404()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await CreateAuthenticatedClientAsync("credentials-delete-notfound@example.com", [RoleConstants.Admin], ct);

        var response = await client.DeleteAsync($"/api/v1/credentials/{Guid.NewGuid()}", ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<Credential> SeedCredentialAsync(string email, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FlowEngineDbContext>();
        var keyProvider = scope.ServiceProvider.GetRequiredService<ICryptoKeyProvider>();
        var encryptionService = scope.ServiceProvider.GetRequiredService<ICredentialEncryptionService>();

        var user = await dbContext.Set<User>().FirstAsync(u => u.Email == email, ct);
        var project = new Project
        {
            Name = "Test Project",
            CreatedBy = user.Id,
        };
        dbContext.Projects.Add(project);
        await dbContext.SaveChangesAsync(ct);

        var key = keyProvider.GetKey();
        var credential = new Credential
        {
            Name = "Test Credential",
            Type = "apiKey",
            ProjectId = project.Id,
            Data = new Dictionary<string, EncryptedField>
            {
                ["apiKey"] = encryptionService.Encrypt("secret", key),
            },
            KeyVersion = "v1",
        };
        dbContext.Credentials.Add(credential);
        await dbContext.SaveChangesAsync(ct);
        return credential;
    }
}

using System.Net;
using System.Net.Http.Json;
using FlowEngine.Application.Dtos;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Authorization;
using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Identity;
using FlowEngine.Host.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FlowEngine.Host.Tests.Credentials;

public class CredentialEnsureTests : HostIntegrationTestBase
{
    public CredentialEnsureTests(FlowEngineWebApplicationFactory factory)
        : base(factory, builder =>
        {
            builder.UseSetting("ExecutionCleanup:Enabled", "false");
            builder.ConfigureServices(services =>
            {
                services.Replace(ServiceDescriptor.Singleton<ICryptoKeyProvider, TestCryptoKeyProvider>());
            });
        })
    {
    }

    [Fact]
    public async Task Ensure_NewCredential_ReturnsCreated201()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await CreateAuthenticatedClientAsync("jwt-credential-ensure-create@example.com", [RoleConstants.Admin], ct);

        var dto = new CreateCredentialDto
        {
            Name = "Ensure API Key",
            Type = "apiKey",
            Fields = new Dictionary<string, string> { ["apiKey"] = "sk-create" },
        };

        var response = await client.PostAsJsonAsync("/api/v1/credentials/ensure", dto, ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<CredentialDto>(TestJsonOptions, ct);
        Assert.NotNull(result);
        Assert.Equal("Ensure API Key", result!.Name);
        Assert.Equal("sk-create", result.Fields["apiKey"]);
    }

    [Fact]
    public async Task GetTypes_ReturnsOkWithBuiltInCredentialTypes()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await CreateAuthenticatedClientAsync("jwt-credential-types@example.com", [RoleConstants.Admin], ct);

        var response = await client.GetAsync("/api/v1/credentials/types", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var types = await response.Content.ReadFromJsonAsync<System.Text.Json.Nodes.JsonArray>(TestJsonOptions, ct);
        Assert.NotNull(types);
        Assert.Contains(types!, t => (string?)t!["name"] == "apiKey");
        Assert.Contains(types!, t => (string?)t!["name"] == "database");
        Assert.Contains(types!, t => (string?)t!["name"] == "oauth2");
    }

    [Fact]
    public async Task Ensure_ExistingCredential_ReturnsOk200AndUpdatesFields()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = "jwt-credential-ensure-update@example.com";
        var client = await CreateAuthenticatedClientAsync(email, [RoleConstants.Admin], ct);
        var credential = await SeedCredentialAsync(email, "Ensure Update Key", "apiKey", new Dictionary<string, string> { ["apiKey"] = "sk-old" }, ct);

        var dto = new CreateCredentialDto
        {
            Name = credential.Name,
            Type = credential.Type,
            Fields = new Dictionary<string, string> { ["apiKey"] = "sk-new" },
        };

        var response = await client.PostAsJsonAsync("/api/v1/credentials/ensure", dto, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<CredentialDto>(TestJsonOptions, ct);
        Assert.NotNull(result);
        Assert.Equal(credential.Id, result!.Id);
        Assert.Equal("sk-new", result.Fields["apiKey"]);
    }

    [Fact]
    public async Task Ensure_Viewer_ReturnsForbidden()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await CreateAuthenticatedClientAsync("jwt-credential-ensure-viewer@example.com", [RoleConstants.Viewer], ct);

        var dto = new CreateCredentialDto
        {
            Name = "Ensure Key",
            Type = "apiKey",
            Fields = [],
        };

        var response = await client.PostAsJsonAsync("/api/v1/credentials/ensure", dto, ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Ensure_WithoutAuthentication_ReturnsUnauthorized()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = _factory.CreateClient();

        var dto = new CreateCredentialDto
        {
            Name = "Ensure Key",
            Type = "apiKey",
            Fields = [],
        };

        var response = await client.PostAsJsonAsync("/api/v1/credentials/ensure", dto, ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<Credential> SeedCredentialAsync(
        string email,
        string name,
        string type,
        Dictionary<string, string> fields,
        CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FlowEngineDbContext>();

        var user = dbContext.Set<User>().First(u => u.Email == email);

        var credential = new Credential
        {
            Name = name,
            Type = type,
            Data = fields.ToDictionary(
                kvp => kvp.Key,
                kvp => new EncryptedField { CipherText = $"encrypted:{kvp.Value}", Nonce = "nonce", Tag = "tag" }),
            KeyVersion = "v1",
        };
        dbContext.Credentials.Add(credential);
        await dbContext.SaveChangesAsync(ct);
        return credential;
    }

    private sealed class TestCryptoKeyProvider : ICryptoKeyProvider
    {
        public string CurrentVersion => "v1";

        public byte[] GetKey() =>
            Convert.FromHexString("000102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E1F");

        public byte[] GetKey(string keyVersion) =>
            string.IsNullOrEmpty(keyVersion) || string.Equals(keyVersion, "v1", StringComparison.OrdinalIgnoreCase)
                ? GetKey()
                : throw new System.Security.Cryptography.CryptographicException($"未知密钥版本 {keyVersion}");
    }
}

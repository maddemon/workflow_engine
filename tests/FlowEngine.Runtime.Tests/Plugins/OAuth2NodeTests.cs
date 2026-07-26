using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Plugins.Standard;
using FlowEngine.Runtime.Credentials;

namespace FlowEngine.Runtime.Tests.Plugins;

public sealed class OAuth2NodeTests
{
    [Fact]
    public async Task ExecuteAsync_MaterializesManagedToken()
    {
        var node = new OAuth2Node { CredentialName = "my-oauth2" };
        var context = CreateContext(new StubCredentialAccessor());

        var result = await ((INodeType)node).ExecuteAsync(context, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Single(result.Output.Items);
        var data = result.Output.Items[0].Data as JsonObject;
        Assert.NotNull(data);
        Assert.Equal("managed-tok", data["accessToken"]?.GetValue<string>());
        Assert.Equal("Bearer", data["tokenType"]?.GetValue<string>());
        Assert.NotNull(data["expiresAt"]);
    }

    [Fact]
    public async Task ExecuteAsync_WrappedAccessor_AutoFetchesToken()
    {
        var inner = new StubCredentialAccessor();
        var tokenService = new FakeOAuth2TokenService();
        var wrappedAccessor = new OAuth2CredentialAccessor(inner, tokenService);
        var node = new OAuth2Node { CredentialName = "my-oauth2" };
        var context = CreateContext(wrappedAccessor);

        var result = await ((INodeType)node).ExecuteAsync(context, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var data = result.Output.Items[0].Data as JsonObject;
        Assert.NotNull(data);
        Assert.Equal("fetched-tok", data["accessToken"]?.GetValue<string>());
    }

    [Fact]
    public async Task ExecuteAsync_MissingCredentialName_ReturnsError()
    {
        var node = new OAuth2Node { CredentialName = "" };
        var context = CreateContext(new StubCredentialAccessor());

        var result = await ((INodeType)node).ExecuteAsync(context, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Equal("MissingCredentialName", result.Error?.Code);
    }

    private static NodeExecutionContext CreateContext(ICredentialAccessor accessor)
    {
        return new NodeExecutionContext
        {
            Node = new NodeDefinition
            {
                Id = "OAuth2",
                TypeName = "oauth2",
                Name = "OAuth2",
                Parameters = new Dictionary<string, object>(),
                Ports = [],
                ErrorStrategy = ErrorStrategy.Terminate
            },
            ExecutionId = Guid.NewGuid(),
            Inputs = new Dictionary<string, DataBatch>
            {
                [FlowConstants.PortNames.Input] = new()
                {
                    Items =
                    [
                        new DataItem
                        {
                            Data = new JsonObject(),
                            Success = true,
                            SourceIndex = 0
                        }
                    ]
                }
            },
            RawParameters = new Dictionary<string, object>(),
            ResolvedParameters = new Dictionary<string, object>(),
            Credentials = accessor,
            CancellationToken = CancellationToken.None
        };
    }

    private sealed class StubCredentialAccessor : ICredentialAccessor
    {
        public Task<CredentialValue> GetCredentialAsync(Guid credentialId, CancellationToken ct = default) =>
            Task.FromResult(CreateValue());

        public Task<CredentialValue?> GetCredentialByNameAsync(string name, CancellationToken ct = default) =>
            Task.FromResult<CredentialValue?>(CreateValue());

        private static CredentialValue CreateValue() => new()
        {
            Name = "my-oauth2",
            Type = "oauth2",
            Fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["tokenUrl"] = "http://example.com/token",
                ["clientId"] = "cid",
                ["clientSecret"] = "cs",
                ["accessToken"] = "managed-tok",
                ["tokenType"] = "Bearer",
                ["expiresIn"] = "3600",
                ["expiresAt"] = DateTime.UtcNow.AddHours(1).ToString("O")
            }
        };
    }

    private sealed class FakeOAuth2TokenService : IOAuth2TokenService
    {
        public Task<OAuth2TokenResponse> GetTokenAsync(OAuth2TokenRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new OAuth2TokenResponse
            {
                AccessToken = "fetched-tok",
                TokenType = "Bearer",
                ExpiresIn = 3600
            });

        public Task<OAuth2TokenResponse> GetOrRefreshTokenAsync(string cacheKey, OAuth2TokenRequest request, CancellationToken cancellationToken = default) =>
            GetTokenAsync(request, cancellationToken);
    }
}

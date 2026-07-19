using System.Net;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Http;
using FlowEngine.Core.Scripting;

namespace FlowEngine.Core.Tests;

public class HttpTests
{
    [Fact]
    public void HttpExecutionRequest_Properties_RoundTrip()
    {
        var request = new HttpExecutionRequest
        {
            Url = "https://example.com",
            Method = HttpMethod.Post,
            AuthMode = HttpRequestAuthMode.BearerToken,
            CredentialId = "cred-1",
            QueryParameterName = "token",
            Headers = new Dictionary<string, string> { ["Accept"] = "application/json" },
            BodyContent = "{}",
            SuccessWhen = new Script { Source = "true" }
        };

        Assert.Equal("https://example.com", request.Url);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(HttpRequestAuthMode.BearerToken, request.AuthMode);
        Assert.Equal("cred-1", request.CredentialId);
        Assert.Equal("token", request.QueryParameterName);
        Assert.Single(request.Headers);
        Assert.Equal("{}", request.BodyContent);
        Assert.NotNull(request.SuccessWhen);
    }

    [Fact]
    public void HttpExecutionRequest_Defaults_AreExpected()
    {
        var request = new HttpExecutionRequest();

        Assert.Equal(string.Empty, request.Url);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(HttpRequestAuthMode.None, request.AuthMode);
        Assert.Null(request.CredentialId);
    }
}

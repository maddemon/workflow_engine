using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FlowEngine.Application.Dtos;
using FlowEngine.Application.Identity;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Authorization;
using FlowEngine.Core.Data;
using FlowEngine.Core.Identity;
using FlowEngine.Host.Tests;
using FlowEngine.Host.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace FlowEngine.Host.Tests.Controllers;

public class AuthControllerTests : HostIntegrationTestBase
{
    public AuthControllerTests(FlowEngineWebApplicationFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task Register_ReturnsForbidden()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/register",
            new RegisterRequest { Email = "register@example.com", Password = "P@ssw0rd" },
            ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Login_ValidCredentials_ReturnsTokenAndCookie()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = "login@example.com";
        var password = "StrongP@ss1";
        await SeedUserAsync(email, password, ct);
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginRequest { Email = email, Password = password },
            ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(response.Headers.GetValues("Set-Cookie").FirstOrDefault(c => c.Contains("fe_auth")));
        var result = await response.Content.ReadFromJsonAsync<LoginResult>(TestJsonOptions, ct);
        Assert.NotNull(result);
        Assert.True(result!.Success);
        Assert.NotNull(result.Token);
    }

    [Fact]
    public async Task Login_InvalidCredentials_ReturnsUnauthorized()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = "login-bad@example.com";
        await SeedUserAsync(email, "StrongP@ss1", ct);
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginRequest { Email = email, Password = "wrong" },
            ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetMe_Authenticated_ReturnsCurrentUser()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await CreateAuthenticatedClientAsync("me@example.com", [RoleConstants.Admin], ct);

        var response = await client.GetAsync("/api/v1/auth/me", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<UserDto>(TestJsonOptions, ct);
        Assert.NotNull(result);
        Assert.Equal("me@example.com", result!.Email);
    }

    [Fact]
    public async Task Logout_Authenticated_ReturnsOk()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await CreateAuthenticatedClientAsync("logout@example.com", [RoleConstants.Admin], ct);

        var response = await client.PostAsync("/api/v1/auth/logout", null, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Login_EmptyEmail_ReturnsUnauthorized()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginRequest { Email = string.Empty, Password = "P@ssw0rd" },
            ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<LoginResult>(TestJsonOptions, ct);
        Assert.NotNull(result);
        Assert.False(result!.Success);
    }

    [Fact]
    public async Task Login_EmptyPassword_ReturnsUnauthorized()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginRequest { Email = "empty-password@example.com", Password = string.Empty },
            ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<LoginResult>(TestJsonOptions, ct);
        Assert.NotNull(result);
        Assert.False(result!.Success);
    }

    [Fact]
    public async Task Login_NonExistentUser_ReturnsUnauthorized()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginRequest { Email = "no-user@example.com", Password = "P@ssw0rd" },
            ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<LoginResult>(TestJsonOptions, ct);
        Assert.NotNull(result);
        Assert.False(result!.Success);
    }

    [Fact]
    public async Task Login_DisabledAccount_ReturnsUnauthorized()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = "disabled@example.com";
        var password = "StrongP@ss1";
        await SeedUserAsync(email, password, ct, isActive: false);
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginRequest { Email = email, Password = password },
            ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<LoginResult>(TestJsonOptions, ct);
        Assert.NotNull(result);
        Assert.False(result!.Success);
    }

    [Fact]
    public async Task Register_DuplicateEmail_ReturnsForbidden()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = "duplicate-register@example.com";
        await SeedUserAsync(email, "StrongP@ss1", ct);
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/register",
            new RegisterRequest { Email = email, Password = "AnotherP@ss1" },
            ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

}

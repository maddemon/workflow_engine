using System.Net;
using System.Net.Http.Json;
using FlowEngine.Application.Dtos;
using FlowEngine.Application.Identity;
using FlowEngine.Core.Authorization;
using FlowEngine.Core.Data;
using FlowEngine.Core.Identity;
using FlowEngine.Host.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FlowEngine.Host.Tests.Controllers;

public class UsersControllerTests : HostIntegrationTestBase
{
    public UsersControllerTests(FlowEngineWebApplicationFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task GetRoles_ExistingUser_ReturnsOkWithRoles()
    {
        var ct = TestContext.Current.CancellationToken;
        var adminClient = await CreateAuthenticatedClientAsync("admin-getroles@example.com", [RoleConstants.Admin], ct);
        var targetUser = await SeedUserWithRoleAsync("target-getroles@example.com", RoleConstants.Viewer, ct);

        var response = await adminClient.GetAsync($"/api/v1/users/{targetUser.Id}/roles", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<IReadOnlyList<string>>(TestJsonOptions, ct);
        Assert.NotNull(result);
        Assert.Contains(RoleConstants.Viewer, result);
    }

    [Fact]
    public async Task AssignRole_Valid_ReturnsOk()
    {
        var ct = TestContext.Current.CancellationToken;
        var adminClient = await CreateAuthenticatedClientAsync("admin-assign@example.com", [RoleConstants.Admin], ct);
        var targetUser = await SeedUserAsync("target-assign@example.com", ct);

        var response = await adminClient.PostAsJsonAsync(
            $"/api/v1/users/{targetUser.Id}/roles",
            new AssignRoleRequest { Role = RoleConstants.Viewer },
            ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AssignRole_InvalidRole_ReturnsBadRequest()
    {
        var ct = TestContext.Current.CancellationToken;
        var adminClient = await CreateAuthenticatedClientAsync("admin-assign-bad@example.com", [RoleConstants.Admin], ct);
        var targetUser = await SeedUserAsync("target-assign-bad@example.com", ct);

        var response = await adminClient.PostAsJsonAsync(
            $"/api/v1/users/{targetUser.Id}/roles",
            new AssignRoleRequest { Role = "NotARealRole" },
            ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RevokeRole_Existing_ReturnsNoContent()
    {
        var ct = TestContext.Current.CancellationToken;
        var adminClient = await CreateAuthenticatedClientAsync("admin-revoke@example.com", [RoleConstants.Admin], ct);
        var targetUser = await SeedUserWithRoleAsync("target-revoke@example.com", RoleConstants.Viewer, ct);

        var response = await adminClient.DeleteAsync($"/api/v1/users/{targetUser.Id}/roles/{RoleConstants.Viewer}", ct);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task GetRoles_NonAdmin_ReturnsForbidden()
    {
        var ct = TestContext.Current.CancellationToken;
        var viewerClient = await CreateAuthenticatedClientAsync("viewer-users@example.com", [RoleConstants.Viewer], ct);
        var targetUser = await SeedUserAsync("target-viewer@example.com", ct);

        var response = await viewerClient.GetAsync($"/api/v1/users/{targetUser.Id}/roles", ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<User> SeedUserAsync(string email, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FlowEngineDbContext>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var user = new User
        {
            Email = email,
            UserName = email.Split('@')[0],
            DisplayName = email,
            PasswordHash = passwordHasher.HashPassword("StrongP@ss1"),
            IsActive = true,
        };
        dbContext.Set<User>().Add(user);
        await dbContext.SaveChangesAsync(ct);
        return user;
    }

    private async Task<User> SeedUserWithRoleAsync(string email, string role, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FlowEngineDbContext>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var user = new User
        {
            Email = email,
            UserName = email.Split('@')[0],
            DisplayName = email,
            PasswordHash = passwordHasher.HashPassword("StrongP@ss1"),
            IsActive = true,
        };
        dbContext.Set<User>().Add(user);
        await dbContext.SaveChangesAsync(ct);
        dbContext.Set<UserRole>().Add(new UserRole { UserId = user.Id, Role = role });
        await dbContext.SaveChangesAsync(ct);
        return user;
    }
}

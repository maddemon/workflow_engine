using System.Net;
using System.Net.Http.Json;
using FlowEngine.Application.Dtos;
using FlowEngine.Core.Authorization;
using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Identity;
using FlowEngine.Host.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FlowEngine.Host.Tests.Controllers;

public class ProjectsControllerTests : HostIntegrationTestBase
{
    public ProjectsControllerTests(FlowEngineWebApplicationFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task GetAll_AuthenticatedAdmin_ReturnsOkWithProjects()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = "projects-getall@example.com";
        var client = await CreateAuthenticatedClientAsync(email, [RoleConstants.Admin], ct);
        var project = await SeedProjectAsync(email, ct);

        var response = await client.GetAsync("/api/v1/projects", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<PagedResult<ProjectDto>>(TestJsonOptions, ct);
        Assert.NotNull(result);
        Assert.Contains(result.Items, p => p.Id == project.Id);
    }

    [Fact]
    public async Task GetAll_ReturnsPagedShape_NotBareArray()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = "projects-paged@example.com";
        var client = await CreateAuthenticatedClientAsync(email, [RoleConstants.Admin], ct);
        var project = await SeedProjectAsync(email, ct);

        var response = await client.GetAsync("/api/v1/projects?page=1&pageSize=20", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<PagedResult<ProjectDto>>(TestJsonOptions, ct);
        Assert.NotNull(result);
        // PagedResult 契约：必须包含 items / totalCount / page / pageSize，而非裸数组。
        Assert.NotNull(result.Items);
        Assert.Contains(result.Items, p => p.Id == project.Id);
        Assert.Equal(1, result.Page);
        Assert.Equal(20, result.PageSize);
        Assert.True(result.TotalCount >= 1);
    }

    [Fact]
    public async Task GetById_Existing_ReturnsOk()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = "projects-getbyid@example.com";
        var client = await CreateAuthenticatedClientAsync(email, [RoleConstants.Admin], ct);
        var project = await SeedProjectAsync(email, ct);

        var response = await client.GetAsync($"/api/v1/projects/{project.Id}", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ProjectDto>(TestJsonOptions, ct);
        Assert.NotNull(result);
        Assert.Equal(project.Id, result!.Id);
    }

    [Fact]
    public async Task GetById_NotFound_Returns404()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await CreateAuthenticatedClientAsync("projects-notfound@example.com", [RoleConstants.Admin], ct);

        var response = await client.GetAsync($"/api/v1/projects/{Guid.NewGuid()}", ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Create_ValidDto_ReturnsCreated()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await CreateAuthenticatedClientAsync("projects-create@example.com", [RoleConstants.Admin], ct);
        var dto = new CreateProjectDto { Name = "New Project", Description = "Description" };

        var response = await client.PostAsJsonAsync("/api/v1/projects", dto, ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ProjectDto>(TestJsonOptions, ct);
        Assert.NotNull(result);
        Assert.Equal(dto.Name, result!.Name);
    }

    [Fact]
    public async Task Create_InvalidDto_ReturnsBadRequest()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await CreateAuthenticatedClientAsync("projects-create-invalid@example.com", [RoleConstants.Admin], ct);
        var dto = new CreateProjectDto { Name = string.Empty };

        var response = await client.PostAsJsonAsync("/api/v1/projects", dto, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_Existing_ReturnsOk()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = "projects-update@example.com";
        var client = await CreateAuthenticatedClientAsync(email, [RoleConstants.Admin], ct);
        var project = await SeedProjectAsync(email, ct);
        var dto = new UpdateProjectDto { Name = "Updated Name", Description = "Updated Description" };

        var response = await client.PutAsJsonAsync($"/api/v1/projects/{project.Id}", dto, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ProjectDto>(TestJsonOptions, ct);
        Assert.NotNull(result);
        Assert.Equal(dto.Name, result!.Name);
    }

    [Fact]
    public async Task Update_NotFound_Returns404()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await CreateAuthenticatedClientAsync("projects-update-notfound@example.com", [RoleConstants.Admin], ct);
        var dto = new UpdateProjectDto { Name = "Updated Name" };

        var response = await client.PutAsJsonAsync($"/api/v1/projects/{Guid.NewGuid()}", dto, ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_Existing_ReturnsNoContent()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = "projects-delete@example.com";
        var client = await CreateAuthenticatedClientAsync(email, [RoleConstants.Admin], ct);
        var project = await SeedProjectAsync(email, ct);

        var response = await client.DeleteAsync($"/api/v1/projects/{project.Id}", ct);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Delete_NotFound_Returns404()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await CreateAuthenticatedClientAsync("projects-delete-notfound@example.com", [RoleConstants.Admin], ct);

        var response = await client.DeleteAsync($"/api/v1/projects/{Guid.NewGuid()}", ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<Project> SeedProjectAsync(string email, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FlowEngineDbContext>();

        var user = await dbContext.Set<User>().FirstAsync(u => u.Email == email, ct);
        var project = new Project
        {
            Name = "Test Project",
            Description = "Test Description",
            CreatedBy = user.Id,
        };
        dbContext.Projects.Add(project);
        await dbContext.SaveChangesAsync(ct);
        return project;
    }
}

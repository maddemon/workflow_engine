using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using FlowEngine.Application.Dtos;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Authorization;
using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Identity;
using FlowEngine.Host.Controllers;
using FlowEngine.Host.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FlowEngine.Host.Tests.Controllers;

public class FilesControllerTests : HostIntegrationTestBase
{
    public FilesControllerTests(FlowEngineWebApplicationFactory factory)
        : base(factory, builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.Replace(ServiceDescriptor.Singleton<IFileStorage>(new FakeFileStorage()));
            });
        })
    {
    }

    [Fact]
    public async Task Upload_ValidFile_ReturnsOk()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = "files-upload@example.com";
        var client = await CreateAuthenticatedClientAsync(email, [RoleConstants.Admin], ct);
        var projectId = await SeedProjectAsync(email, ct);
        var content = new ByteArrayContent("file content"u8.ToArray());
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain");
        var form = new MultipartFormDataContent
        {
            { content, "file", "test.txt" },
        };

        var response = await client.PostAsync($"/api/v1/files/upload?projectId={projectId}", form, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<UploadFileResult>(TestJsonOptions, ct);
        Assert.NotNull(result);
        Assert.Equal("test.txt", result!.FileName);
    }

    [Fact]
    public async Task Upload_EmptyFile_ReturnsBadRequest()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = "files-upload-empty@example.com";
        var client = await CreateAuthenticatedClientAsync(email, [RoleConstants.Admin], ct);
        var projectId = await SeedProjectAsync(email, ct);
        var content = new ByteArrayContent([]);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain");
        var form = new MultipartFormDataContent
        {
            { content, "file", "empty.txt" },
        };

        var response = await client.PostAsync($"/api/v1/files/upload?projectId={projectId}", form, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetById_Existing_ReturnsOk()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = "files-get@example.com";
        var client = await CreateAuthenticatedClientAsync(email, [RoleConstants.Admin], ct);
        var file = await SeedFileAsync(email, ct);

        var response = await client.GetAsync($"/api/v1/files/{file.Id}", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<StoredFileDto>(TestJsonOptions, ct);
        Assert.NotNull(result);
        Assert.Equal(file.Id, result!.Id);
    }

    [Fact]
    public async Task GetById_NotFound_Returns404()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await CreateAuthenticatedClientAsync("files-get-notfound@example.com", [RoleConstants.Admin], ct);

        var response = await client.GetAsync($"/api/v1/files/{Guid.NewGuid()}", ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Download_Existing_ReturnsFile()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = "files-download@example.com";
        var client = await CreateAuthenticatedClientAsync(email, [RoleConstants.Admin], ct);
        var file = await SeedFileAsync(email, ct);

        var response = await client.GetAsync($"/api/v1/files/{file.Id}/download", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Download_NotFound_Returns404()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await CreateAuthenticatedClientAsync("files-download-notfound@example.com", [RoleConstants.Admin], ct);

        var response = await client.GetAsync($"/api/v1/files/{Guid.NewGuid()}/download", ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_Existing_ReturnsNoContent()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = "files-delete@example.com";
        var client = await CreateAuthenticatedClientAsync(email, [RoleConstants.Admin], ct);
        var file = await SeedFileAsync(email, ct);

        var response = await client.DeleteAsync($"/api/v1/files/{file.Id}", ct);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Delete_NotFound_Returns404()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await CreateAuthenticatedClientAsync("files-delete-notfound@example.com", [RoleConstants.Admin], ct);

        var response = await client.DeleteAsync($"/api/v1/files/{Guid.NewGuid()}", ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetAll_ByProject_ReturnsOk()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = "files-getall@example.com";
        var client = await CreateAuthenticatedClientAsync(email, [RoleConstants.Admin], ct);
        var file = await SeedFileAsync(email, ct);

        var response = await client.GetAsync($"/api/v1/files?projectId={file.ProjectId}", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<PagedResult<StoredFileDto>>(TestJsonOptions, ct);
        Assert.NotNull(result);
        Assert.Contains(result.Items, f => f.Id == file.Id);
    }

    [Fact]
    public async Task GetAll_ByProject_ReturnsPagedShape_NotBareArray()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = "files-paged@example.com";
        var client = await CreateAuthenticatedClientAsync(email, [RoleConstants.Admin], ct);
        var file = await SeedFileAsync(email, ct);

        var response = await client.GetAsync($"/api/v1/files?projectId={file.ProjectId}&page=1&pageSize=20", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<PagedResult<StoredFileDto>>(TestJsonOptions, ct);
        Assert.NotNull(result);
        // PagedResult 契约：必须包含 items / totalCount / page / pageSize，而非裸数组。
        Assert.NotNull(result.Items);
        Assert.Contains(result.Items, f => f.Id == file.Id);
        Assert.Equal(1, result.Page);
        Assert.Equal(20, result.PageSize);
        Assert.True(result.TotalCount >= 1);
    }

    [Fact]
    public void Delete_Endpoint_Requires_OperationDelete_Permission()
    {
        // 删除文件的授权粒度应与"删除"语义一致，而非复用的写入权限。
        var method = typeof(FilesController).GetMethod(
            nameof(FilesController.Delete),
            BindingFlags.Public | BindingFlags.Instance,
            [typeof(Guid), typeof(CancellationToken)]);
        Assert.NotNull(method);

        var attribute = method!.GetCustomAttribute<AuthorizePermissionAttribute>();
        Assert.NotNull(attribute);
        Assert.Equal(Scope.File, attribute!.Scope);
        Assert.Equal(Operation.Delete, attribute.Operation);
    }

    private async Task<Guid> SeedProjectAsync(string email, CancellationToken ct)
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
        return project.Id;
    }

    private async Task<StoredFile> SeedFileAsync(string email, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FlowEngineDbContext>();
        var fileStorage = scope.ServiceProvider.GetRequiredService<IFileStorage>();

        var user = await dbContext.Set<User>().FirstAsync(u => u.Email == email, ct);
        var project = new Project
        {
            Name = "Test Project",
            CreatedBy = user.Id,
        };
        dbContext.Projects.Add(project);
        await dbContext.SaveChangesAsync(ct);

        await using var content = new MemoryStream("file content"u8.ToArray());
        var storagePath = await fileStorage.SaveAsync("test.txt", content, project.Id.ToString(), ct);

        var file = new StoredFile
        {
            FileName = "test.txt",
            ContentType = "text/plain",
            Size = "file content"u8.Length,
            StoragePath = storagePath,
            ProjectId = project.Id,
            UploadedBy = user.Id,
        };
        dbContext.StoredFiles.Add(file);
        await dbContext.SaveChangesAsync(ct);
        return file;
    }

    private sealed class FakeFileStorage : IFileStorage
    {
        private readonly Dictionary<string, byte[]> _files = [];

        public Task<string> SaveAsync(string fileName, Stream content, string projectId, CancellationToken ct = default)
        {
            var fileId = Guid.NewGuid().ToString("N");
            using var ms = new MemoryStream();
            content.CopyTo(ms);
            _files[fileId] = ms.ToArray();
            return Task.FromResult(fileId);
        }

        public Task<Stream?> ReadAsync(string fileId, CancellationToken ct = default)
        {
            if (_files.TryGetValue(fileId, out var bytes))
            {
                return Task.FromResult<Stream?>(new MemoryStream(bytes));
            }

            return Task.FromResult<Stream?>(null);
        }

        public Task<bool> DeleteAsync(string fileId, CancellationToken ct = default)
        {
            return Task.FromResult(_files.Remove(fileId));
        }

        public Task<bool> ExistsAsync(string fileId, CancellationToken ct = default)
        {
            return Task.FromResult(_files.ContainsKey(fileId));
        }
    }
}

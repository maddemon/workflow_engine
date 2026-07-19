using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FlowEngine.Application.Dtos;
using FlowEngine.Application.Identity;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Authorization;
using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Identity;
using FlowEngine.Host.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace FlowEngine.Host.Tests.Controllers;

public class FilesControllerTests : IClassFixture<FlowEngineWebApplicationFactory>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _tempRoot;

    public FilesControllerTests(FlowEngineWebApplicationFactory factory)
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "flowengine-tests", Guid.NewGuid().ToString());
        var dbDirectory = Path.Combine(_tempRoot, "db");
        var auditDirectory = Path.Combine(_tempRoot, "audit");
        Directory.CreateDirectory(dbDirectory);
        Directory.CreateDirectory(auditDirectory);

        var dbPath = Path.Combine(dbDirectory, "flowengine.db");
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Default", $"Data Source={dbPath};Mode=ReadWriteCreate");
            builder.UseSetting("Audit:LogPath", auditDirectory);
            builder.ConfigureServices(services =>
            {
                services.Replace(ServiceDescriptor.Singleton<IScheduleManager, NoOpScheduleManager>());
                services.RemoveAll<IHostedService>();
                services.Replace(ServiceDescriptor.Singleton<IFileStorage>(new FakeFileStorage()));
            });
        });

        _factory.ClientOptions.BaseAddress = new Uri("http://localhost");
    }

    public void Dispose()
    {
        _factory.Dispose();
        try
        {
            Directory.Delete(_tempRoot, true);
        }
        catch
        {
            // 忽略清理临时目录时的错误，不影响测试结果。
        }
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
        var result = await response.Content.ReadFromJsonAsync<IReadOnlyList<StoredFileDto>>(TestJsonOptions, ct);
        Assert.NotNull(result);
        Assert.Contains(result, f => f.Id == file.Id);
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync(string email, IReadOnlyList<string>? roles = null, CancellationToken ct = default)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FlowEngineDbContext>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();

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

        if (roles is not null)
        {
            foreach (var role in roles)
            {
                dbContext.Set<UserRole>().Add(new UserRole { UserId = user.Id, Role = role });
            }

            await dbContext.SaveChangesAsync(ct);
        }

        var token = tokenService.GenerateAccessToken(user.Id, user.Email, roles ?? []);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return client;
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

    private static JsonSerializerOptions TestJsonOptions => HostTestJsonOptions.Default;

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

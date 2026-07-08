using FlowEngine.Application.Authorization;
using FlowEngine.Application.Files;
using FlowEngine.Application.Identity;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Authorization;
using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FlowEngine.Application.Tests.Files;

public sealed class FileServiceTests : IDisposable
{
    private readonly FlowEngineDbContext _dbContext;
    private readonly FakeFileStorage _fileStorage;
    private readonly FakeUserContext _userContext;
    private readonly FileStorageOptions _options;
    private readonly FileService _service;

    public FileServiceTests()
    {
        var options = new DbContextOptionsBuilder<FlowEngineDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbContext = new FlowEngineDbContext(options);
        _fileStorage = new FakeFileStorage();
        _userContext = new FakeUserContext();
        _options = new FileStorageOptions();
        _service = new FileService(_dbContext, _fileStorage, _userContext, new FakeResourceAuthorizationService(), Options.Create(_options));
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    [Fact]
    public async Task UploadAsync_ValidFile_CreatesRecord()
    {
        var ct = TestContext.Current.CancellationToken;
        var projectId = Guid.NewGuid();
        var content = new MemoryStream("test content"u8.ToArray());

        var result = await _service.UploadAsync("test.txt", content, "text/plain", projectId, ct);

        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal("test.txt", result.FileName);
        Assert.Equal(12, result.Size);

        var record = await _dbContext.StoredFiles.FindAsync([result.Id], ct);
        Assert.NotNull(record);
        Assert.Equal(projectId, record!.ProjectId);
        Assert.Equal(_userContext.UserId!.Value, record.UploadedBy);
        Assert.True(_fileStorage.SavedFiles.Count > 0);
    }

    [Fact]
    public async Task UploadAsync_NullFileName_Throws()
    {
        var ct = TestContext.Current.CancellationToken;
        var stream = new MemoryStream("test"u8.ToArray());

        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => _service.UploadAsync(null!, stream, "text/plain", Guid.NewGuid(), ct));
    }

    [Fact]
    public async Task UploadAsync_NullStream_Throws()
    {
        var ct = TestContext.Current.CancellationToken;
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _service.UploadAsync("test.txt", null!, "text/plain", Guid.NewGuid(), ct));
    }

    [Fact]
    public async Task UploadAsync_ExceedsMaxSize_Throws_InvalidOperation()
    {
        var ct = TestContext.Current.CancellationToken;
        var projectId = Guid.NewGuid();
        _options.MaxFileSizeBytes = 10; // 10 字节上限
        var content = new MemoryStream(new byte[11]); // 11 字节，超限

        await Assert.ThrowsAsync<BusinessException>(
            () => _service.UploadAsync("big.bin", content, "application/octet-stream", projectId, ct));
    }

    [Fact]
    public async Task UploadAsync_DisallowedContentType_Throws_InvalidOperation()
    {
        var ct = TestContext.Current.CancellationToken;
        var projectId = Guid.NewGuid();
        _options.AllowedContentTypes = ["text/plain"];
        var content = new MemoryStream("test"u8.ToArray());

        await Assert.ThrowsAsync<BusinessException>(
            () => _service.UploadAsync("a.exe", content, "application/octet-stream", projectId, ct));
    }

    [Fact]
    public async Task UploadAsync_AllowedContentType_Succeeds()
    {
        var ct = TestContext.Current.CancellationToken;
        var projectId = Guid.NewGuid();
        _options.AllowedContentTypes = ["text/plain"];
        var content = new MemoryStream("test"u8.ToArray());

        var result = await _service.UploadAsync("a.txt", content, "text/plain", projectId, ct);

        Assert.NotEqual(Guid.Empty, result.Id);
    }

    [Fact]
    public async Task UploadAsync_NullContentType_WithWhitelistEnabled_Throws_InvalidOperation()
    {
        // 复现 I-1：白名单启用时，contentType 为 null 应被拒绝（fail-closed，防绕过）。
        var ct = TestContext.Current.CancellationToken;
        var projectId = Guid.NewGuid();
        _options.AllowedContentTypes = ["text/plain"];
        var content = new MemoryStream("test"u8.ToArray());

        await Assert.ThrowsAsync<BusinessException>(
            () => _service.UploadAsync("a.txt", content, null, projectId, ct));
    }

    [Fact]
    public async Task UploadAsync_EmptyContentType_WithWhitelistEnabled_Throws_InvalidOperation()
    {
        // 复现 I-1：白名单启用时，contentType 为空字符串也应被拒绝。
        var ct = TestContext.Current.CancellationToken;
        var projectId = Guid.NewGuid();
        _options.AllowedContentTypes = ["text/plain"];
        var content = new MemoryStream("test"u8.ToArray());

        await Assert.ThrowsAsync<BusinessException>(
            () => _service.UploadAsync("a.txt", content, "", projectId, ct));
    }

    [Fact]
    public async Task UploadAsync_NullContentType_WithoutWhitelist_Succeeds()
    {
        // 反向验证：未启用白名单时，contentType 为 null 应允许（保持兼容）。
        var ct = TestContext.Current.CancellationToken;
        var projectId = Guid.NewGuid();
        // _options.AllowedContentTypes 默认为空数组，等价于未启用白名单
        var content = new MemoryStream("test"u8.ToArray());

        var result = await _service.UploadAsync("a.txt", content, null, projectId, ct);

        Assert.NotEqual(Guid.Empty, result.Id);
    }

    [Fact]
    public async Task GetAsync_ExistingFile_ReturnsDto()
    {
        var ct = TestContext.Current.CancellationToken;
        var projectId = Guid.NewGuid();
        var content = new MemoryStream("test"u8.ToArray());

        var uploaded = await _service.UploadAsync("test.txt", content, "text/plain", projectId, ct);

        var result = await _service.GetAsync(uploaded.Id, ct);

        Assert.NotNull(result);
        Assert.Equal(uploaded.Id, result!.Id);
        Assert.Equal("test.txt", result.FileName);
        Assert.Equal(projectId, result.ProjectId);
    }

    [Fact]
    public async Task GetAsync_NonExistingFile_ReturnsNull()
    {
        var ct = TestContext.Current.CancellationToken;
        var result = await _service.GetAsync(Guid.NewGuid(), ct);
        Assert.Null(result);
    }

    [Fact]
    public async Task DownloadAsync_ExistingFile_ReturnsStream()
    {
        var ct = TestContext.Current.CancellationToken;
        var projectId = Guid.NewGuid();
        var content = new MemoryStream("downloadable"u8.ToArray());

        var uploaded = await _service.UploadAsync("test.txt", content, "text/plain", projectId, ct);

        var stream = await _service.DownloadAsync(uploaded.Id, ct);

        Assert.NotNull(stream);
        await using var _ = stream!;
        using var reader = new StreamReader(stream);
        Assert.Equal("downloadable", await reader.ReadToEndAsync(ct));
    }

    [Fact]
    public async Task DownloadAsync_NonExistingFile_ReturnsNull()
    {
        var ct = TestContext.Current.CancellationToken;
        var result = await _service.DownloadAsync(Guid.NewGuid(), ct);
        Assert.Null(result);
    }

    [Fact]
    public async Task DeleteAsync_ExistingFile_DeletesRecord()
    {
        var ct = TestContext.Current.CancellationToken;
        var projectId = Guid.NewGuid();
        var content = new MemoryStream("to delete"u8.ToArray());

        var uploaded = await _service.UploadAsync("test.txt", content, "text/plain", projectId, ct);

        var deleted = await _service.DeleteAsync(uploaded.Id, ct);

        Assert.True(deleted);
        var record = await _dbContext.StoredFiles.FindAsync([uploaded.Id], ct);
        Assert.NotNull(record);
        Assert.True(record!.Deleted);
        Assert.True(_fileStorage.DeletedFiles.Count > 0);
    }

    [Fact]
    public async Task DeleteAsync_NonExistingFile_ReturnsFalse()
    {
        var ct = TestContext.Current.CancellationToken;
        var result = await _service.DeleteAsync(Guid.NewGuid(), ct);
        Assert.False(result);
    }

    [Fact]
    public async Task GetAllByProjectAsync_ReturnsProjectFiles()
    {
        var ct = TestContext.Current.CancellationToken;
        var projectId = Guid.NewGuid();
        var otherProjectId = Guid.NewGuid();

        await _service.UploadAsync("a.txt", new MemoryStream("a"u8.ToArray()), "text/plain", projectId, ct);
        await _service.UploadAsync("b.txt", new MemoryStream("b"u8.ToArray()), "text/plain", projectId, ct);
        await _service.UploadAsync("c.txt", new MemoryStream("c"u8.ToArray()), "text/plain", otherProjectId, ct);

        var result = await _service.GetAllByProjectAsync(projectId, ct);

        Assert.Equal(2, result.Count);
        Assert.All(result, f => Assert.Equal(projectId, f.ProjectId));
    }

    [Fact]
    public async Task GetAllByProjectAsync_ExcludesDeletedFiles()
    {
        var ct = TestContext.Current.CancellationToken;
        var projectId = Guid.NewGuid();

        var uploaded = await _service.UploadAsync("a.txt", new MemoryStream("a"u8.ToArray()), "text/plain", projectId, ct);
        await _service.UploadAsync("b.txt", new MemoryStream("b"u8.ToArray()), "text/plain", projectId, ct);

        await _service.DeleteAsync(uploaded.Id, ct);

        var result = await _service.GetAllByProjectAsync(projectId, ct);

        Assert.Single(result);
    }

    private sealed class FakeFileStorage : IFileStorage
    {
        private readonly Dictionary<string, MemoryStream> _files = [];
        public List<string> SavedFiles { get; } = [];
        public List<string> DeletedFiles { get; } = [];

        public Task<string> SaveAsync(string fileName, Stream content, string projectId, CancellationToken ct = default)
        {
            var fileId = Guid.NewGuid().ToString("N");
            var ms = new MemoryStream();
            content.CopyTo(ms);
            ms.Position = 0;
            _files[fileId] = ms;
            SavedFiles.Add(fileId);
            return Task.FromResult(fileId);
        }

        public Task<Stream?> ReadAsync(string fileId, CancellationToken ct = default)
        {
            if (_files.TryGetValue(fileId, out var stream))
            {
                stream.Position = 0;
                return Task.FromResult<Stream?>(stream);
            }
            return Task.FromResult<Stream?>(null);
        }

        public Task<bool> DeleteAsync(string fileId, CancellationToken ct = default)
        {
            if (_files.Remove(fileId))
            {
                DeletedFiles.Add(fileId);
                return Task.FromResult(true);
            }
            return Task.FromResult(false);
        }

        public Task<bool> ExistsAsync(string fileId, CancellationToken ct = default)
        {
            return Task.FromResult(_files.ContainsKey(fileId));
        }
    }

    private sealed class FakeUserContext : IUserContext
    {
        private readonly Guid _userId = Guid.NewGuid();
        public bool IsAuthenticated => true;
        public Guid? UserId => _userId;
        public string? Email => "test@test.com";
        public IReadOnlyList<string> Roles => [RoleConstants.Admin];
    }

    private sealed class FakeResourceAuthorizationService : IResourceAuthorizationService
    {
        public Task<bool> CanAccessWorkflowAsync(Guid userId, Guid workflowId, Operation operation, CancellationToken ct = default)
            => Task.FromResult(true);

        public Task<bool> CanAccessCredentialAsync(Guid userId, Guid credentialId, Operation operation, CancellationToken ct = default)
            => Task.FromResult(true);

        public Task<bool> CanAccessExecutionAsync(Guid userId, Guid executionId, Operation operation, CancellationToken ct = default)
            => Task.FromResult(true);

        public Task<bool> CanAccessTriggerAsync(Guid userId, Guid triggerId, Operation operation, CancellationToken ct = default)
            => Task.FromResult(true);

        public Task<bool> CanAccessProjectAsync(Guid userId, Guid projectId, Operation operation, CancellationToken ct = default)
            => Task.FromResult(true);

        public bool ShouldMaskCredentialValues(IReadOnlyList<string> roles) => false;
    }

}

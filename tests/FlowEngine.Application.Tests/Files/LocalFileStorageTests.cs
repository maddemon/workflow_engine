using FlowEngine.Infrastructure.Storage;

namespace FlowEngine.Application.Tests.Files;

public sealed class LocalFileStorageTests : IDisposable
{
    private readonly string _tempDir;
    private readonly LocalFileStorage _storage;

    public LocalFileStorageTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"flowengine_test_{Guid.NewGuid():N}");
        _storage = new LocalFileStorage(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task SaveAsync_WritesFileToDisk()
    {
        var ct = TestContext.Current.CancellationToken;
        var projectId = Guid.NewGuid().ToString();
        var content = "Hello, World!"u8.ToArray();

        using var stream = new MemoryStream(content);
        var fileId = await _storage.SaveAsync("test.txt", stream, projectId, ct);

        Assert.False(string.IsNullOrEmpty(fileId));
        Assert.True(await _storage.ExistsAsync(fileId, ct));
    }

    [Fact]
    public async Task ReadAsync_ExistingFile_ReturnsContent()
    {
        var ct = TestContext.Current.CancellationToken;
        var projectId = Guid.NewGuid().ToString();
        var content = "Test content"u8.ToArray();

        using (var stream = new MemoryStream(content))
        {
            await _storage.SaveAsync("test.txt", stream, projectId, ct);
        }

        var fileId = await _storage.SaveAsync("test.txt", new MemoryStream(content), projectId, ct);
        using var readStream = await _storage.ReadAsync(fileId, ct);

        Assert.NotNull(readStream);
        using var reader = new StreamReader(readStream);
        var result = await reader.ReadToEndAsync(ct);
        Assert.Equal("Test content", result);
    }

    [Fact]
    public async Task ReadAsync_NonExistingFile_ReturnsNull()
    {
        var ct = TestContext.Current.CancellationToken;
        var result = await _storage.ReadAsync("nonexistent", ct);
        Assert.Null(result);
    }

    [Fact]
    public async Task DeleteAsync_ExistingFile_RemovesFile()
    {
        var ct = TestContext.Current.CancellationToken;
        var projectId = Guid.NewGuid().ToString();
        var content = "To delete"u8.ToArray();

        using (var stream = new MemoryStream(content))
        {
            await _storage.SaveAsync("test.txt", stream, projectId, ct);
        }

        var fileId = await _storage.SaveAsync("test.txt", new MemoryStream(content), projectId, ct);

        var deleted = await _storage.DeleteAsync(fileId, ct);

        Assert.True(deleted);
        Assert.False(await _storage.ExistsAsync(fileId, ct));
    }

    [Fact]
    public async Task DeleteAsync_NonExistingFile_ReturnsFalse()
    {
        var ct = TestContext.Current.CancellationToken;
        var result = await _storage.DeleteAsync("nonexistent", ct);
        Assert.False(result);
    }

    [Fact]
    public async Task ExistsAsync_ExistingFile_ReturnsTrue()
    {
        var ct = TestContext.Current.CancellationToken;
        var projectId = Guid.NewGuid().ToString();
        var content = "Exists"u8.ToArray();

        using (var stream = new MemoryStream(content))
        {
            await _storage.SaveAsync("test.txt", stream, projectId, ct);
        }

        var fileId = await _storage.SaveAsync("test.txt", new MemoryStream(content), projectId, ct);

        Assert.True(await _storage.ExistsAsync(fileId, ct));
    }

    [Fact]
    public async Task ExistsAsync_NonExistingFile_ReturnsFalse()
    {
        var ct = TestContext.Current.CancellationToken;
        Assert.False(await _storage.ExistsAsync("nonexistent", ct));
    }

    [Fact]
    public async Task SaveAsync_ProjectIdWithTraversal_Throws()
    {
        var ct = TestContext.Current.CancellationToken;
        var stream = new MemoryStream("test"u8.ToArray());

        await Assert.ThrowsAsync<ArgumentException>(
            () => _storage.SaveAsync("test.txt", stream, "../escape", ct));
    }

    [Fact]
    public async Task SaveAsync_DifferentProjects_StoresSeparately()
    {
        var ct = TestContext.Current.CancellationToken;
        var project1 = Guid.NewGuid().ToString();
        var project2 = Guid.NewGuid().ToString();

        using var stream1 = new MemoryStream("file1"u8.ToArray());
        using var stream2 = new MemoryStream("file2"u8.ToArray());

        var fileId1 = await _storage.SaveAsync("a.txt", stream1, project1, ct);
        var fileId2 = await _storage.SaveAsync("a.txt", stream2, project2, ct);

        Assert.NotEqual(fileId1, fileId2);

        using var read1 = await _storage.ReadAsync(fileId1, ct);
        using var read2 = await _storage.ReadAsync(fileId2, ct);

        Assert.NotNull(read1);
        Assert.NotNull(read2);

        using var reader1 = new StreamReader(read1);
        using var reader2 = new StreamReader(read2);
        Assert.Equal("file1", await reader1.ReadToEndAsync(ct));
        Assert.Equal("file2", await reader2.ReadToEndAsync(ct));
    }
}

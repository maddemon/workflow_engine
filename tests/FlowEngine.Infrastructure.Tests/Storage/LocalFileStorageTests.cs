using System.Text;
using FlowEngine.Infrastructure.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FlowEngine.Infrastructure.Tests.Storage;

public sealed class LocalFileStorageTests : IDisposable
{
    private readonly string _basePath;
    private readonly LocalFileStorage _storage;

    public LocalFileStorageTests()
    {
        _basePath = Path.Combine(Path.GetTempPath(), $"flowengine-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_basePath);
        _storage = new LocalFileStorage(_basePath, NullLogger<LocalFileStorage>.Instance);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_basePath))
            {
                Directory.Delete(_basePath, recursive: true);
            }
        }
        catch
        {
            // 临时目录清理失败不影响测试结果。
        }
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SaveAsync_WithValidInput_ReturnsRelativePathAndWritesFile()
    {
        var projectId = Guid.NewGuid().ToString();
        const string fileName = "hello.txt";
        const string content = "world";

        var fileId = await _storage.SaveAsync(
            fileName, new MemoryStream(Encoding.UTF8.GetBytes(content)), projectId, Ct);

        Assert.False(string.IsNullOrWhiteSpace(fileId));
        Assert.StartsWith($"{projectId}/", fileId);
        Assert.Contains("_hello.txt", fileId);
        var fullPath = Path.Combine(_basePath, fileId);
        Assert.True(File.Exists(fullPath));
        Assert.Equal(content, await File.ReadAllTextAsync(fullPath, Ct));
    }

    [Fact]
    public async Task ReadAsync_AfterSave_ReturnsMatchingContent()
    {
        var projectId = Guid.NewGuid().ToString();
        const string content = "roundtrip content";
        var fileId = await _storage.SaveAsync(
            "doc.bin", new MemoryStream(Encoding.UTF8.GetBytes(content)), projectId, Ct);

        var stream = await _storage.ReadAsync(fileId, Ct);

        Assert.NotNull(stream);
        using var reader = new StreamReader(stream);
        Assert.Equal(content, await reader.ReadToEndAsync(Ct));
    }

    [Fact]
    public async Task ReadAsync_MissingFile_ReturnsNull()
    {
        var stream = await _storage.ReadAsync($"{Guid.NewGuid():N}/missing.txt", Ct);

        Assert.Null(stream);
    }

    [Fact]
    public async Task ExistsAsync_ExistingFile_ReturnsTrue()
    {
        var projectId = Guid.NewGuid().ToString();
        var fileId = await _storage.SaveAsync(
            "exists.txt", new MemoryStream("x"u8.ToArray()), projectId, Ct);

        Assert.True(await _storage.ExistsAsync(fileId, Ct));
    }

    [Fact]
    public async Task ExistsAsync_MissingFile_ReturnsFalse()
    {
        Assert.False(await _storage.ExistsAsync($"{Guid.NewGuid():N}/ghost.txt", Ct));
    }

    [Fact]
    public async Task DeleteAsync_ExistingFile_RemovesFileAndReturnsTrue()
    {
        var projectId = Guid.NewGuid().ToString();
        var fileId = await _storage.SaveAsync(
            "delete.txt", new MemoryStream("x"u8.ToArray()), projectId, Ct);

        var result = await _storage.DeleteAsync(fileId, Ct);

        Assert.True(result);
        Assert.False(await _storage.ExistsAsync(fileId, Ct));
    }

    [Fact]
    public async Task DeleteAsync_MissingFile_ReturnsFalse()
    {
        var result = await _storage.DeleteAsync($"{Guid.NewGuid():N}/ghost.txt", Ct);

        Assert.False(result);
    }

    [Fact]
    public async Task SaveAsync_InvalidProjectId_ThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _storage.SaveAsync("file.txt", new MemoryStream("x"u8.ToArray()), "not-a-guid", Ct));
    }

    [Fact]
    public async Task SaveAsync_EmptyFileName_ThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _storage.SaveAsync("", new MemoryStream("x"u8.ToArray()), Guid.NewGuid().ToString(), Ct));
    }

    [Fact]
    public async Task SaveAsync_NullContent_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _storage.SaveAsync("file.txt", null!, Guid.NewGuid().ToString(), Ct));
    }

    [Fact]
    public async Task ReadAsync_PathTraversalOutsideBase_ReturnsNull()
    {
        var stream = await _storage.ReadAsync("../../../etc/passwd", Ct);

        Assert.Null(stream);
    }

    [Fact]
    public async Task SaveAsync_InvalidFileNameCharacters_AreSanitized()
    {
        var projectId = Guid.NewGuid().ToString();
        const string fileName = "a<b:c>d/e\"f.txt";

        var fileId = await _storage.SaveAsync(
            fileName, new MemoryStream("x"u8.ToArray()), projectId, Ct);

        Assert.DoesNotContain('<', fileId);
        Assert.DoesNotContain('>', fileId);
        Assert.DoesNotContain(':', fileId);
        Assert.DoesNotContain('"', fileId);
        Assert.True(File.Exists(Path.Combine(_basePath, fileId)));
    }

    [Fact]
    public async Task ReadAsync_OldFormatFileId_FindsFileByScan()
    {
        var projectId = Guid.NewGuid().ToString();
        var fileIdOnly = Guid.NewGuid().ToString("N");
        var directory = Path.Combine(_basePath, projectId);
        Directory.CreateDirectory(directory);
        var filePath = Path.Combine(directory, $"{fileIdOnly}_legacy.txt");
        await File.WriteAllTextAsync(filePath, "legacy", Ct);

        var stream = await _storage.ReadAsync(fileIdOnly, Ct);

        Assert.NotNull(stream);
        using var reader = new StreamReader(stream);
        Assert.Equal("legacy", await reader.ReadToEndAsync(Ct));
    }
}

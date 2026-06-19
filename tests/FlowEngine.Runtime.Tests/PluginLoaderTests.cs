using FlowEngine.Runtime.Registry;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlowEngine.Runtime.Tests;

public class PluginLoaderTests : IDisposable
{
    private readonly string _tempDirectory;

    public PluginLoaderTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        // 插件 DLL 被加载后会被 AssemblyLoadContext 锁定，无法立即删除临时目录。
        // 每个测试使用独立 GUID 目录，避免冲突；由操作系统在进程退出后清理。
    }

    [Fact]
    public void Empty_Directory_Returns_No_Nodes()
    {
        var loader = new PluginLoader(_tempDirectory, NullLogger<PluginLoader>.Instance);

        var nodes = loader.LoadNodes();

        Assert.Empty(nodes);
    }

    [Fact]
    public void Missing_Directory_Returns_No_Nodes()
    {
        var missingPath = Path.Combine(_tempDirectory, "missing");
        var loader = new PluginLoader(missingPath, NullLogger<PluginLoader>.Instance);

        var nodes = loader.LoadNodes();

        Assert.Empty(nodes);
    }

    [Fact]
    public void Valid_Plugin_Directory_Loads_Nodes()
    {
        CopyTestPluginTo(_tempDirectory);
        var loader = new PluginLoader(_tempDirectory, NullLogger<PluginLoader>.Instance);

        var nodes = loader.LoadNodes();

        Assert.Single(nodes);
    }

    [Fact]
    public void Broken_Dll_Is_Skipped_And_Valid_Dll_Is_Loaded()
    {
        CopyTestPluginTo(_tempDirectory);
        File.WriteAllBytes(Path.Combine(_tempDirectory, "broken.dll"), [0x4D, 0x5A, 0x00]);

        var loader = new PluginLoader(_tempDirectory, NullLogger<PluginLoader>.Instance);
        var nodes = loader.LoadNodes();

        Assert.Single(nodes);
    }

    [Fact]
    public void Relative_Plugins_Path_Loads_Nodes()
    {
        var relativeDir = $"plugins-{Guid.NewGuid():N}";
        Directory.CreateDirectory(relativeDir);
        try
        {
            CopyTestPluginTo(relativeDir);

            var loader = new PluginLoader(relativeDir, NullLogger<PluginLoader>.Instance);
            var nodes = loader.LoadNodes();

            Assert.Single(nodes);
        }
        finally
        {
            try
            {
                Directory.Delete(relativeDir, recursive: true);
            }
            catch (UnauthorizedAccessException)
            {
                // 插件 DLL 可能被锁定，忽略清理失败。
            }
        }
    }

    private static void CopyTestPluginTo(string targetDirectory)
    {
        var testPluginAssembly = typeof(TestPlugin.TestNode).Assembly;
        var sourcePath = testPluginAssembly.Location;
        var targetPath = Path.Combine(targetDirectory, Path.GetFileName(sourcePath));
        File.Copy(sourcePath, targetPath);
    }
}

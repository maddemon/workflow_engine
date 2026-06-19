using System.Reflection;
using System.Runtime.Loader;

namespace FlowEngine.Runtime.Registry;

/// <summary>
/// 插件程序集加载上下文，用于隔离加载单个插件 DLL。
/// </summary>
internal sealed class PluginLoadContext : AssemblyLoadContext
{
    private readonly string _pluginDirectory;

    /// <summary>
    /// 初始化插件加载上下文。
    /// </summary>
    /// <param name="pluginPath">插件 DLL 完整路径。</param>
    public PluginLoadContext(string pluginPath)
        : base(isCollectible: true)
    {
        ArgumentException.ThrowIfNullOrEmpty(pluginPath);
        _pluginDirectory = Path.GetDirectoryName(Path.GetFullPath(pluginPath))
            ?? throw new ArgumentException("无法从插件路径获取目录。", nameof(pluginPath));
    }

    /// <inheritdoc />
    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // 若程序集已被默认加载上下文加载（如 FlowEngine.Core、系统程序集），
        // 返回 null 委托默认上下文解析，避免同一程序集在不同 ALC 中重复加载
        // 导致类型标识不一致（typeof(INodeType).IsAssignableFrom 返回 false）。
        var defaultAssembly = AssemblyLoadContext.Default.Assemblies
            .FirstOrDefault(a => a.GetName().Name == assemblyName.Name);
        if (defaultAssembly is not null)
        {
            return null;
        }

        var assemblyPath = Path.Combine(_pluginDirectory, $"{assemblyName.Name}.dll");
        if (File.Exists(assemblyPath))
        {
            return LoadFromAssemblyPath(assemblyPath);
        }

        return null;
    }
}

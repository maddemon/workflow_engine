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
    /// 宿主共享程序集名称（精确匹配）。这些程序集由宿主默认上下文统一提供，
    /// 插件必须复用同一份，禁止从插件目录加载私有副本（task-013 P2）。
    /// </summary>
    private static readonly HashSet<string> SharedAssemblyNames =
    [
        "System.ClientModel",
        "OpenAI",
        "Acornima",
        "Jint",
        "Newtonsoft.Json",
        "System.IdentityModel.Tokens.Jwt",
    ];

    /// <summary>
    /// 宿主共享程序集名称前缀。以这些前缀开头的程序集同样视为共享程序集。
    /// </summary>
    private static readonly string[] SharedAssemblyPrefixes =
    [
        "Azure.",
        "Microsoft.IdentityModel",
        "Microsoft.Extensions",
    ];

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
        // 共享程序集（OpenAI / System.ClientModel / Azure.* / Jint 等）必须由宿主默认上下文
        // 统一解析。若此处允许从插件目录加载私有副本，plugins/ 中残留的旧版本
        // （如 System.ClientModel 1.8.0.0 缺少 ClientSettings）会与宿主版本冲突，
        // 触发 TypeLoadException（task-013 P2）。因此直接复用默认上下文中已加载的实例；
        // 若默认上下文尚未加载，返回 null 交由其从宿主依赖闭包解析，绝不回退到插件目录。
        if (IsSharedAssembly(assemblyName.Name))
        {
            return AssemblyLoadContext.Default.Assemblies
                .FirstOrDefault(a => a.GetName().Name == assemblyName.Name);
        }

        // 非共享程序集：若默认上下文已加载则复用，避免同一程序集在不同 ALC 中重复加载
        // 导致类型标识不一致（typeof(INodeType).IsAssignableFrom 返回 false）；否则从插件目录加载。
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

    private static bool IsSharedAssembly(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        if (SharedAssemblyNames.Contains(name))
        {
            return true;
        }

        return SharedAssemblyPrefixes.Any(prefix =>
            name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }
}

using System.Reflection;
using System.Runtime.Versioning;
using FlowEngine.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace FlowEngine.Runtime.Registry;

/// <summary>
/// 插件加载器，负责扫描插件目录并使用独立加载上下文加载节点类型。
/// </summary>
public sealed class PluginLoader
{
    private readonly string _pluginsDirectory;
    private readonly ILogger<PluginLoader> _logger;

    // B9：宿主自身编译时的目标框架，用于拦截与宿主 TFM 不一致的陈旧插件。
    // 取自当前程序集的 TargetFrameworkAttribute，避免硬编码，随宿主 TFM 自动同步。
    private static readonly string HostFrameworkName =
        typeof(PluginLoader).Assembly
            .GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName
        ?? string.Empty;

    /// <summary>
    /// 初始化插件加载器。
    /// </summary>
    /// <param name="pluginsDirectory">插件目录路径。</param>
    /// <param name="logger">日志记录器。</param>
    public PluginLoader(string pluginsDirectory, ILogger<PluginLoader> logger)
    {
        ArgumentException.ThrowIfNullOrEmpty(pluginsDirectory);
        _pluginsDirectory = pluginsDirectory;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 扫描插件目录并加载所有 <see cref="INodeType"/> 实现。
    /// </summary>
    /// <returns>加载成功的节点类型实例列表。</returns>
    public IReadOnlyList<INodeType> LoadNodes()
    {
        var nodes = new List<INodeType>();

        if (!Directory.Exists(_pluginsDirectory))
        {
            _logger.LogWarning("插件目录不存在: {PluginsDirectory}", _pluginsDirectory);
            return nodes;
        }

        var dllPaths = Directory.EnumerateFiles(_pluginsDirectory, "*.dll").ToList();
        _logger.LogInformation("开始扫描插件目录，共发现 {Count} 个 DLL", dllPaths.Count);

        foreach (var dllPath in dllPaths)
        {
            try
            {
                var context = new PluginLoadContext(dllPath);
                var assembly = context.LoadFromAssemblyPath(Path.GetFullPath(dllPath));

                // B9：拦截与宿主目标框架不兼容的陈旧插件，给出明确告警而非晦涩的类型加载异常。
                // .NETStandard 插件始终兼容；.NETCoreApp 低版本向前兼容（RollForward）。
                var pluginFramework = assembly.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName;
                if (!string.IsNullOrEmpty(pluginFramework) &&
                    !string.IsNullOrEmpty(HostFrameworkName) &&
                    !IsFrameworkCompatible(pluginFramework, HostFrameworkName))
                {
                    _logger.LogWarning(
                        "跳过插件 {DllPath}：目标框架 {PluginFramework} 与宿主 {HostFramework} 不兼容。",
                        dllPath, pluginFramework, HostFrameworkName);
                    continue;
                }

                var nodeTypes = assembly.GetTypes()
                    .Where(t => typeof(INodeType).IsAssignableFrom(t)
                                && t is { IsClass: true, IsAbstract: false })
                    .ToList();

                foreach (var type in nodeTypes)
                {
                    var instance = (INodeType?)Activator.CreateInstance(type);
                    if (instance is not null)
                    {
                        nodes.Add(instance);
                        _logger.LogDebug("已加载节点类型 {TypeName} 从 {DllPath}", instance.TypeName, dllPath);
                    }
                }
            }
            catch (ReflectionTypeLoadException ex)
            {
                _logger.LogWarning(ex, "加载插件 {DllPath} 时发生类型加载异常", dllPath);
            }
            catch (BadImageFormatException ex)
            {
                _logger.LogWarning(ex, "插件 {DllPath} 不是有效的 .NET 程序集", dllPath);
            }
            catch (FileLoadException ex)
            {
                _logger.LogWarning(ex, "加载插件 {DllPath} 失败", dllPath);
            }
            catch (TypeLoadException ex)
            {
                _logger.LogWarning(ex, "加载插件 {DllPath} 时无法加载所需类型", dllPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "加载插件 {DllPath} 时发生未预期异常", dllPath);
            }
        }

        _logger.LogInformation("插件扫描完成，成功加载 {Count} 个节点类型", nodes.Count);
        return nodes;
    }

    /// <summary>
    /// 判断插件目标框架是否与宿主兼容。
    /// </summary>
    /// <remarks>
    /// 兼容规则：
    /// 1. .NETStandard 插件始终兼容（设计为跨框架）。
    /// 2. 同一系列（如 .NETCoreApp）低版本向前兼容（RollForward 机制）。
    /// 3. 完全相同框架名兼容。
    /// </remarks>
    private static bool IsFrameworkCompatible(string pluginFramework, string hostFramework)
    {
        // 完全匹配
        if (string.Equals(pluginFramework, hostFramework, StringComparison.OrdinalIgnoreCase))
            return true;

        // 解析框架：格式如 ".NETCoreApp,Version=v10.0" 或 ".NETStandard,Version=v2.0"
        if (!TryParseFramework(pluginFramework, out var pluginId, out var pluginVersion) ||
            !TryParseFramework(hostFramework, out var hostId, out var hostVersion) ||
            pluginVersion is null || hostVersion is null)
            return false;

        // .NETStandard 始终兼容
        if (pluginId.Equals(".NETStandard", StringComparison.OrdinalIgnoreCase))
            return true;

        // 同一系列且插件版本 <= 宿主版本时向前兼容
        if (pluginId.Equals(hostId, StringComparison.OrdinalIgnoreCase))
            return pluginVersion <= hostVersion;

        return false;
    }

    /// <summary>
    /// 解析 TFM 字符串，提取标识符和版本号。
    /// </summary>
    private static bool TryParseFramework(string frameworkName, out string identifier, out Version? version)
    {
        identifier = string.Empty;
        version = null;

        // 格式：".NETCoreApp,Version=v10.0"
        var parts = frameworkName.Split(',', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            return false;

        identifier = parts[0].Trim();

        // 查找 Version=vX.Y.Z
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (trimmed.StartsWith("Version=", StringComparison.OrdinalIgnoreCase))
            {
                var versionStr = trimmed["Version=".Length..].TrimStart('v');
                return Version.TryParse(versionStr, out version);
            }
        }

        return false;
    }
}

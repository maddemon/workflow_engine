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

                // B9：拦截与宿主目标框架不一致的陈旧插件，给出明确告警而非晦涩的类型加载异常。
                var pluginFramework = assembly.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName;
                if (!string.IsNullOrEmpty(pluginFramework) &&
                    !string.IsNullOrEmpty(HostFrameworkName) &&
                    !string.Equals(pluginFramework, HostFrameworkName, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning(
                        "跳过插件 {DllPath}：目标框架 {PluginFramework} 与宿主 {HostFramework} 不一致，可能是陈旧插件。",
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
}

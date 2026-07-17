using System;
using System.Reflection;
using Microsoft.Extensions.Localization;

namespace FlowEngine.Resources.Localization;

/// <summary>
/// 基于嵌入式 JSON 资源的 IStringLocalizerFactory 实现。
/// 资源文件作为嵌入资源打包在程序集中。
/// </summary>
public sealed class JsonStringLocalizerFactory : IStringLocalizerFactory
{
    private readonly Assembly _resourceAssembly;

    public JsonStringLocalizerFactory(Assembly resourceAssembly)
    {
        _resourceAssembly = resourceAssembly;
    }

    public IStringLocalizer Create(Type resourceSource)
    {
        var resourceName = GetResourceName(resourceSource);
        return new JsonStringLocalizer(resourceName, _resourceAssembly);
    }

    public IStringLocalizer Create(string location, string name)
    {
        return new JsonStringLocalizer(name, _resourceAssembly);
    }

    private static string GetResourceName(Type resourceSource)
    {
        // SharedResource -> SharedResource
        // SomeNamespace.SharedResource -> SharedResource
        return resourceSource.Name;
    }
}

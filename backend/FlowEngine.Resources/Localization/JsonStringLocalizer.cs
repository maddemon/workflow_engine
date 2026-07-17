using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Localization;

namespace FlowEngine.Resources.Localization;

/// <summary>
/// 基于嵌入式 JSON 资源的 IStringLocalizer 实现。
/// 资源文件约定：{ResourceName}.json（默认）、{ResourceName}.{culture}.json（特定语言）。
/// </summary>
public sealed class JsonStringLocalizer : IStringLocalizer
{
    private static readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, string>> Cache = new();
    private readonly string _resourceName;
    private readonly Assembly _resourceAssembly;

    public JsonStringLocalizer(string resourceName, Assembly resourceAssembly)
    {
        _resourceName = resourceName;
        _resourceAssembly = resourceAssembly;
    }

    public LocalizedString this[string name]
    {
        get
        {
            ArgumentNullException.ThrowIfNull(name);
            var value = GetString(name);
            return new LocalizedString(name, value ?? name, resourceNotFound: value is null, searchedLocation: $"{_resourceAssembly.GetName().Name}/{_resourceName}");
        }
    }

    public LocalizedString this[string name, params object[] arguments]
    {
        get
        {
            var format = GetString(name) ?? name;
            var value = string.Format(format, arguments);
            return new LocalizedString(name, value, resourceNotFound: format == name, searchedLocation: $"{_resourceAssembly.GetName().Name}/{_resourceName}");
        }
    }

    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
    {
        var culture = CultureInfo.CurrentUICulture;
        var strings = new Dictionary<string, string>();

        if (includeParentCultures)
        {
            var current = culture;
            while (current != null && !current.Equals(CultureInfo.InvariantCulture))
            {
                MergeStrings(strings, current);
                current = current.Parent;
            }
        }
        else
        {
            MergeStrings(strings, culture);
        }

        // 回退到默认（无文化后缀）的 JSON 文件
        MergeDefaultStrings(strings);

        foreach (var kvp in strings)
        {
            yield return new LocalizedString(kvp.Key, kvp.Value, resourceNotFound: false, searchedLocation: _resourceName);
        }
    }

    public IStringLocalizer WithCulture(CultureInfo? culture)
    {
        // 通过设置 CurrentUICulture 来切换语言
        return this;
    }

    private string? GetString(string name)
    {
        // 尝试加载特定文化的字符串
        var culture = CultureInfo.CurrentUICulture;
        var strings = LoadJson(culture);
        if (strings.TryGetValue(name, out var value))
        {
            return value;
        }

        // 回退到父文化
        var parent = culture.Parent;
        while (parent != null && !parent.Equals(CultureInfo.InvariantCulture))
        {
            strings = LoadJson(parent);
            if (strings.TryGetValue(name, out value))
            {
                return value;
            }
            parent = parent.Parent;
        }

        // 回退到默认（无文化后缀）
        strings = LoadDefaultJson();
        if (strings.TryGetValue(name, out value))
        {
            return value;
        }

        return null;
    }

    private void MergeStrings(Dictionary<string, string> target, CultureInfo culture)
    {
        var strings = LoadJson(culture);
        foreach (var kvp in strings)
        {
            target.TryAdd(kvp.Key, kvp.Value);
        }
    }

    private void MergeDefaultStrings(Dictionary<string, string> target)
    {
        var strings = LoadDefaultJson();
        foreach (var kvp in strings)
        {
            target.TryAdd(kvp.Key, kvp.Value);
        }
    }

    private IReadOnlyDictionary<string, string> LoadJson(CultureInfo culture)
    {
        var cacheKey = $"{_resourceName}.{culture.Name}";
        return Cache.GetOrAdd(cacheKey, _ => LoadEmbeddedResource(culture));
    }

    private IReadOnlyDictionary<string, string> LoadDefaultJson()
    {
        var cacheKey = $"{_resourceName}.__default__";
        return Cache.GetOrAdd(cacheKey, _ => LoadEmbeddedResource(null));
    }

    private IReadOnlyDictionary<string, string> LoadEmbeddedResource(CultureInfo? culture)
    {
        // 嵌入资源命名约定：{Namespace}.{FileName}
        // 例如：FlowEngine.Resources.SharedResource.json
        //       FlowEngine.Resources.SharedResource_zh_CN.json
        // 注意：MSBuild 会把 .zh-CN / .zh_CN 都当作卫星程序集文化名，
        //       所以用下划线且不以点分隔：SharedResource_zh_CN.json
        var baseName = _resourceAssembly.GetName().Name;
        var cultureSuffix = culture is null
            ? null
            : culture.Name.Replace('-', '_'); // zh-CN -> zh_CN

        var fileName = cultureSuffix is null
            ? $"{_resourceName}.json"
            : $"{_resourceName}_{cultureSuffix}.json";

        var resourceName = $"{baseName}.{fileName}";

        using var stream = _resourceAssembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            return new Dictionary<string, string>();
        }

        try
        {
            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();
            var doc = JsonDocument.Parse(json);
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    result[prop.Name] = prop.Value.GetString() ?? string.Empty;
                }
            }

            return result;
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }
}

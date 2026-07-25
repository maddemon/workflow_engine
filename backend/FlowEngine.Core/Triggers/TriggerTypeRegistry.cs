using System.ComponentModel;
using FlowEngine.Core.Enums;

namespace FlowEngine.Core.Triggers;

/// <summary>
/// 触发器类型注册表（EXT-2 扩展性缝）。
///
/// 内置类型由 <see cref="TriggerType"/> 枚举播种，新增自定义触发器类型无需修改 Core 枚举：
/// 通过 <see cref="Register"/> 在启动时登记即可。当前触发器分发仍基于 <see cref="TriggerType"/> 枚举，
/// 本注册表作为"未来按类型对象 + handler 注册表"演进的兼容入口；调用方可用 <see cref="IsKnown"/>
/// 在校验环节识别内置与自定义类型。
/// </summary>
public sealed class TriggerTypeRegistry
{
    private readonly Dictionary<string, TriggerTypeMetadata> _types;

    public TriggerTypeRegistry()
    {
        _types = CreateBuiltInTypes().ToDictionary(t => t.Type, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 注册（或覆盖）一个触发器类型。应在应用启动时、接收请求前调用。
    /// </summary>
    /// <param name="type">类型标识（大小写不敏感）。</param>
    /// <param name="displayName">显示名称。</param>
    public void Register(string type, string displayName)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            throw new ArgumentException("触发器类型标识不能为空。", nameof(type));
        }

        _types[type] = new TriggerTypeMetadata(type, displayName);
    }

    /// <summary>
    /// 该类型是否已登记（内置或自定义）。
    /// </summary>
    public bool IsKnown(string type)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            return false;
        }

        return _types.ContainsKey(type);
    }

    /// <summary>
    /// 获取已登记类型的元数据集合。
    /// </summary>
    public IReadOnlyCollection<TriggerTypeMetadata> GetAll() => _types.Values.ToList();

    private static IEnumerable<TriggerTypeMetadata> CreateBuiltInTypes()
    {
        foreach (TriggerType value in Enum.GetValues<TriggerType>())
        {
            var name = value.ToString();
            var description = value.GetType()
                .GetField(name)?
                .GetCustomAttributes(typeof(DescriptionAttribute), false)
                .Cast<DescriptionAttribute>()
                .FirstOrDefault()?.Description ?? name;

            yield return new TriggerTypeMetadata(name, description);
        }
    }
}

/// <summary>
/// 触发器类型元数据。
/// </summary>
/// <param name="Type">类型标识。</param>
/// <param name="DisplayName">显示名称。</param>
public sealed record TriggerTypeMetadata(string Type, string DisplayName);

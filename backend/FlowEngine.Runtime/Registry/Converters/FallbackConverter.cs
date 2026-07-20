using Microsoft.Extensions.Logging;

namespace FlowEngine.Runtime.Registry.Converters;

/// <summary>
/// 兜底转换策略，使用 Convert.ChangeType。
/// </summary>
internal sealed class FallbackConverter : IValueConverter
{
    public bool CanConvert(Type targetType) => true;

    public Task<object?> ConvertAsync(object? value, Type targetType, ParameterHydratorContext context)
    {
        try
        {
            if (targetType == typeof(Guid) && value is string guidString)
            {
                return Task.FromResult<object?>(Guid.Parse(guidString));
            }

            return Task.FromResult<object?>(Convert.ChangeType(value, targetType));
        }
        catch (Exception ex)
        {
            context.Logger?.LogWarning(ex, "类型转换失败：{Value} → {TargetType}。", value, targetType.Name);
            return Task.FromResult<object?>(null);
        }
    }
}

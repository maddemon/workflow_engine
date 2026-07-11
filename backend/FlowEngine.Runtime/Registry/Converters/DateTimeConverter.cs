namespace FlowEngine.Runtime.Registry.Converters;

/// <summary>
/// DateTime/DateTimeOffset 类型转换策略。
/// </summary>
internal sealed class DateTimeConverter : IValueConverter
{
    public bool CanConvert(Type targetType)
        => targetType == typeof(DateTime) || targetType == typeof(DateTimeOffset);

    public Task<object?> ConvertAsync(object? value, Type targetType, ParameterHydratorContext context)
    {
        var str = StringConverter.ConvertToString(value!);
        if (str is null)
        {
            return Task.FromResult<object?>(null);
        }

        object? result = targetType == typeof(DateTimeOffset)
            ? (DateTimeOffset.TryParse(str, out var dto) ? dto : null)
            : (DateTime.TryParse(str, out var dt) ? dt : null);
        return Task.FromResult(result);
    }
}

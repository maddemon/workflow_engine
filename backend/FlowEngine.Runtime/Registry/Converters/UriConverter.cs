namespace FlowEngine.Runtime.Registry.Converters;

/// <summary>
/// Uri 类型转换策略。
/// </summary>
internal sealed class UriConverter : IValueConverter
{
    public bool CanConvert(Type targetType) => targetType == typeof(Uri);

    public Task<object?> ConvertAsync(object? value, Type targetType, ParameterHydratorContext context)
    {
        var str = StringConverter.ConvertToString(value!);
        object? result = str is not null ? new Uri(str, UriKind.RelativeOrAbsolute) : null;
        return Task.FromResult(result);
    }
}

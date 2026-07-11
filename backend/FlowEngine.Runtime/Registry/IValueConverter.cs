namespace FlowEngine.Runtime.Registry;

/// <summary>
/// 参数值类型转换策略。
/// </summary>
internal interface IValueConverter
{
    /// <summary>
    /// 是否能处理目标类型。
    /// </summary>
    bool CanConvert(Type targetType);

    /// <summary>
    /// 转换值到目标类型。
    /// </summary>
    Task<object?> ConvertAsync(object? value, Type targetType, ParameterHydratorContext context);
}

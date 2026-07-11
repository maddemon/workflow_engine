using System.Text.Json;

namespace FlowEngine.Runtime.Registry.Converters;

/// <summary>
/// 数值类型（int/long/double/float）转换策略。
/// </summary>
internal sealed class NumericConverter : IValueConverter
{
    public bool CanConvert(Type targetType)
        => targetType == typeof(int)
            || targetType == typeof(long)
            || targetType == typeof(double)
            || targetType == typeof(float);

    public Task<object?> ConvertAsync(object? value, Type targetType, ParameterHydratorContext context)
    {
        object? result = targetType == typeof(int) ? ConvertToInt(value!)
            : targetType == typeof(long) ? ConvertToLong(value!)
            : targetType == typeof(double) ? ConvertToDouble(value!)
            : ConvertToFloat(value!);
        return Task.FromResult<object?>(result);
    }

    private static int ConvertToInt(object value)
    {
        return value switch
        {
            int i => i,
            long l => ClampToInt(l),
            double d => ClampToInt(d),
            float f => ClampToInt(f),
            string s => int.TryParse(s, out var r) ? r : 0,
            JsonElement element => element.ValueKind == JsonValueKind.Number
                ? ClampToInt(element.GetDouble())
                : int.TryParse(element.GetString(), out var r) ? r : 0,
            _ => Convert.ToInt32(value)
        };
    }

    private static long ConvertToLong(object value)
    {
        return value switch
        {
            long l => l,
            int i => i,
            double d => (long)Math.Clamp(d, long.MinValue, long.MaxValue),
            string s => long.TryParse(s, out var r) ? r : 0,
            JsonElement element => element.ValueKind == JsonValueKind.Number
                ? (long)element.GetDouble()
                : long.TryParse(element.GetString(), out var r) ? r : 0,
            _ => Convert.ToInt64(value)
        };
    }

    private static double ConvertToDouble(object value)
    {
        return value switch
        {
            double d => d,
            int i => i,
            long l => l,
            float f => f,
            string s => double.TryParse(s, out var r) ? r : 0,
            JsonElement element => element.ValueKind == JsonValueKind.Number
                ? element.GetDouble()
                : double.TryParse(element.GetString(), out var r) ? r : 0,
            _ => Convert.ToDouble(value)
        };
    }

    private static float ConvertToFloat(object value)
    {
        return value switch
        {
            float f => f,
            double d => (float)d,
            int i => i,
            string s => float.TryParse(s, out var r) ? r : 0,
            JsonElement element => element.ValueKind == JsonValueKind.Number
                ? (float)element.GetDouble()
                : float.TryParse(element.GetString(), out var r) ? r : 0,
            _ => Convert.ToSingle(value)
        };
    }

    private static int ClampToInt(long value)
    {
        return (int)Math.Clamp(value, int.MinValue, int.MaxValue);
    }

    private static int ClampToInt(double value)
    {
        return (int)Math.Clamp(value, int.MinValue, int.MaxValue);
    }

    private static int ClampToInt(float value)
    {
        return (int)Math.Clamp(value, int.MinValue, int.MaxValue);
    }
}

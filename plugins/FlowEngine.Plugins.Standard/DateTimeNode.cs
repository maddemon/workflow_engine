using System.ComponentModel;
using System.Globalization;
using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Attributes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Exceptions;

namespace FlowEngine.Plugins.Standard;

/// <summary>
/// 日期时间节点，提供五种运算：<c>now</c> / <c>format</c> / <c>add</c> / <c>diff</c> / <c>convertTz</c>。
/// 基于 <see cref="DateTimeOffset"/> 与 <see cref="TimeZoneInfo"/> 实现。
/// 输出为单条 <c>DataItem</c>，字段含 <c>value</c>（格式化字符串）与 <c>timestamp</c>（Unix 毫秒 long）。
/// </summary>
/// <remarks>
/// 参数设计选择：
/// <list type="bullet">
///   <item><description><see cref="Input"/> 与 <see cref="SecondInput"/> 直接以 ISO 8601 / 可解析字符串传入，省略了 JS 表达式求值以贴合"日期字符串"语义，保持简单可测。</description></item>
///   <item><description><see cref="Diff"/> 运算的 <c>timestamp</c> 字段承载两时间的差值绝对值（毫秒），而非 Unix 纪元；<c>value</c> 承载可读的 <see cref="TimeSpan"/> 表示。</description></item>
///   <item><description><see cref="ConvertTz"/> 省略 <see cref="BaseTimezone"/> 时，按 <see cref="Input"/> 自带偏移（或 UTC）解释源时刻。</description></item>
/// </list>
/// </remarks>
[NodeMeta(TypeName = "dateTime", DisplayName = "Date Time", Category = NodeCategory.Data, Icon = "calendar", DefaultIsEntry = false)]
[Port(FlowConstants.PortNames.Input, "Input", PortDirection.Input, PortType.Main)]
[Port(FlowConstants.PortNames.Output, "Output", PortDirection.Output, PortType.Main)]
public sealed class DateTimeNode : NodeBase
{
    /// <summary>
    /// 默认输出格式串（当 <see cref="Format"/> 为空时使用）。
    /// </summary>
    private const string DefaultFormat = "yyyy-MM-dd HH:mm:ss";

    /// <summary>
    /// 运算类型：now | format | add | diff | convertTz。
    /// </summary>
    [Description("Operation: now | format | add | diff | convertTz.")]
    public DateTimeOperation Operation { get; set; } = DateTimeOperation.Now;

    /// <summary>
    /// 输入时间（ISO 8601 或 .NET 可解析的日期时间字符串）。
    /// <c>now</c> 运算可省略；<c>diff</c> 运算中表示起始时间。
    /// </summary>
    [Description("Input datetime (ISO 8601 or parseable string). Omitted for 'now'; for 'diff' it is the start time.")]
    public string? Input { get; set; }

    /// <summary>
    /// <c>diff</c> 运算的结束时间（ISO 8601），与 <see cref="Input"/> 计算差值。
    /// </summary>
    [Description("End time (ISO 8601) for the 'diff' operation.")]
    public string? SecondInput { get; set; }

    /// <summary>
    /// 输出格式串（.NET 自定义日期时间格式，如 "yyyy-MM-dd HH:mm:ss"）。为空时使用默认格式。
    /// </summary>
    [Description("Output format string (.NET custom date/time format). Defaults to 'yyyy-MM-dd HH:mm:ss' when empty.")]
    public string? Format { get; set; }

    /// <summary>
    /// <c>add</c> 运算的增减单位。
    /// </summary>
    [Description("Unit for the 'add' operation: Second | Minute | Hour | Day | Month | Year.")]
    public DateTimeUnit AddUnit { get; set; } = DateTimeUnit.Day;

    /// <summary>
    /// <c>add</c> 运算的增减量（可为负）。
    /// </summary>
    [Description("Amount to add for the 'add' operation (may be negative).")]
    public int AddValue { get; set; }

    /// <summary>
    /// <c>convertTz</c> 运算的目标时区（IANA 或 Windows 时区 ID）。
    /// </summary>
    [Description("Target timezone id (IANA or Windows) for the 'convertTz' operation.")]
    public string? Timezone { get; set; }

    /// <summary>
    /// <c>convertTz</c> 运算的源时区。省略时按 <see cref="Input"/> 自带偏移或 UTC 解释。
    /// </summary>
    [Description("Source timezone id for 'convertTz'. When omitted, Input's offset (or UTC) is used.")]
    public string? BaseTimezone { get; set; }

    /// <inheritdoc />
    public override Task<NodeHandlerOutput> ExecuteAsync(NodeInput input, CancellationToken ct)
    {
        try
        {
            var result = Operation switch
            {
                DateTimeOperation.Now => HandleNow(),
                DateTimeOperation.Format => HandleFormat(),
                DateTimeOperation.Add => HandleAdd(),
                DateTimeOperation.Diff => HandleDiff(),
                DateTimeOperation.ConvertTz => HandleConvertTz(),
                _ => throw new NodeExecutionException("UnknownOperation", $"Unsupported Operation '{Operation}'.")
            };
            return Task.FromResult(result);
        }
        catch (Exception ex) when (ex is not NodeExecutionException)
        {
            throw new NodeExecutionException(FlowConstants.ErrorCodes.UnexpectedError, $"DateTime error: {ex.Message}");
        }
    }

    /// <summary>now：返回当前 UTC 时间戳。</summary>
    private NodeHandlerOutput HandleNow()
    {
        var now = DateTimeOffset.UtcNow;
        return CreateResult(now, now);
    }

    /// <summary>format：按 <see cref="Format"/> 格式化 <see cref="Input"/>。</summary>
    private NodeHandlerOutput HandleFormat()
    {
        var parsed = ParseInputOrThrow(Input);
        return CreateResult(parsed, parsed);
    }

    /// <summary>add：按 <see cref="AddUnit"/> / <see cref="AddValue"/> 对 <see cref="Input"/>（或 now）增减。</summary>
    private NodeHandlerOutput HandleAdd()
    {
        var parsed = ParseInputOrThrow(Input);

        var result = AddUnit switch
        {
            DateTimeUnit.Second => parsed.AddSeconds(AddValue),
            DateTimeUnit.Minute => parsed.AddMinutes(AddValue),
            DateTimeUnit.Hour => parsed.AddHours(AddValue),
            DateTimeUnit.Day => parsed.AddDays(AddValue),
            DateTimeUnit.Month => parsed.AddMonths(AddValue),
            DateTimeUnit.Year => parsed.AddYears(AddValue),
            _ => parsed
        };

        return CreateResult(result, result);
    }

    /// <summary>diff：计算 <see cref="Input"/> 与 <see cref="SecondInput"/> 的差值（毫秒）。</summary>
    private NodeHandlerOutput HandleDiff()
    {
        var start = ParseInputOrThrow(Input);
        var end = ParseInputOrThrow(SecondInput);

        // diff 没有"时间点"语义：timestamp 承载差值绝对值（毫秒），value 承载可读 TimeSpan。
        var diff = end - start;
        var absMilliseconds = (long)Math.Round(Math.Abs(diff.TotalMilliseconds));

        return NodeHandlerOutput.Data(new DataBatch
        {
            Items =
            [
                new DataItem
                {
                    Data = new JsonObject
                    {
                        ["value"] = diff.ToString(),
                        ["timestamp"] = absMilliseconds
                    },
                    Success = true,
                    SourceIndex = 0
                }
            ]
        });
    }

    /// <summary>convertTz：将 <see cref="Input"/> 从 <see cref="BaseTimezone"/> 转换到 <see cref="Timezone"/>。</summary>
    private NodeHandlerOutput HandleConvertTz()
    {
        if (string.IsNullOrWhiteSpace(Timezone))
        {
            throw new NodeExecutionException("MissingTimezone", "Timezone is required for the 'convertTz' operation.");
        }

        var input = ParseInputOrThrow(Input);

        TimeZoneInfo targetTz;
        try
        {
            targetTz = TimeZoneInfo.FindSystemTimeZoneById(Timezone!);
        }
        catch (TimeZoneNotFoundException)
        {
            throw new NodeExecutionException("InvalidTimezone", $"Unknown target timezone '{Timezone}'.");
        }
        catch (InvalidTimeZoneException)
        {
            throw new NodeExecutionException("InvalidTimezone", $"Invalid target timezone '{Timezone}'.");
        }

        DateTimeOffset instantUtc;
        if (!string.IsNullOrWhiteSpace(BaseTimezone))
        {
            TimeZoneInfo sourceTz;
            try
            {
                sourceTz = TimeZoneInfo.FindSystemTimeZoneById(BaseTimezone!);
            }
            catch (TimeZoneNotFoundException)
            {
                throw new NodeExecutionException("InvalidTimezone", $"Unknown base timezone '{BaseTimezone}'.");
            }
            catch (InvalidTimeZoneException)
            {
                throw new NodeExecutionException("InvalidTimezone", $"Invalid base timezone '{BaseTimezone}'.");
            }

            // 将 Input 的"墙上时间"按源时区解释为 UTC 时刻（跳过模糊/无效时段由框架兜底）。
            try
            {
                instantUtc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(input.DateTime, sourceTz));
            }
            catch (ArgumentException ex)
            {
                throw new NodeExecutionException("InvalidInput", $"Input is not a valid time in base timezone: {ex.Message}");
            }
        }
        else
        {
            instantUtc = input.ToUniversalTime();
        }

        var targetLocal = TimeZoneInfo.ConvertTimeFromUtc(instantUtc.UtcDateTime, targetTz);
        var targetOffset = targetTz.GetUtcOffset(instantUtc.UtcDateTime);
        var targetDisplay = new DateTimeOffset(targetLocal.Ticks, targetOffset);

        return CreateResult(targetDisplay, instantUtc);
    }

    /// <summary>
    /// 构造单条结果：<c>value</c> 为按格式化的字符串，<c>timestamp</c> 为 <paramref name="instant"/> 的 Unix 毫秒。
    /// 格式串非法时返回 <c>InvalidFormat</c> 错误结果。
    /// </summary>
    private NodeHandlerOutput CreateResult(DateTimeOffset display, DateTimeOffset instant)
    {
        string formatted;
        try
        {
            formatted = display.ToString(ResolveFormat(), CultureInfo.InvariantCulture);
        }
        catch (FormatException ex)
        {
            throw new NodeExecutionException("InvalidFormat", $"Invalid format string: {ex.Message}");
        }

        return NodeHandlerOutput.Data(new DataBatch
        {
            Items =
            [
                new DataItem
                {
                    Data = new JsonObject
                    {
                        ["value"] = formatted,
                        ["timestamp"] = instant.ToUnixTimeMilliseconds()
                    },
                    Success = true,
                    SourceIndex = 0
                }
            ]
        });
    }

    /// <summary>
    /// 解析输入时间；为空时回退到当前 UTC 时刻；解析失败抛出 <see cref="NodeExecutionException"/>。
    /// </summary>
    private DateTimeOffset ParseInputOrThrow(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return DateTimeOffset.UtcNow;
        }

        if (!DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            throw new NodeExecutionException("InvalidInput", $"Unable to parse datetime '{raw}'.");
        }

        return parsed;
    }

    /// <summary>解析输出格式串，为空时使用默认格式。</summary>
    private string ResolveFormat() => string.IsNullOrWhiteSpace(Format) ? DefaultFormat : Format!;
}

/// <summary>
/// 日期时间运算类型。
/// </summary>
public enum DateTimeOperation
{
    /// <summary>返回当前 UTC 时间戳。</summary>
    [Description("Current UTC timestamp.")]
    Now,

    /// <summary>按格式串格式化输入时间。</summary>
    [Description("Format the input datetime.")]
    Format,

    /// <summary>按单位对输入时间增减。</summary>
    [Description("Add/subtract a time unit to the input.")]
    Add,

    /// <summary>计算两个时间的差值（毫秒）。</summary>
    [Description("Difference between two datetimes (ms).")]
    Diff,

    /// <summary>在时区之间换算。</summary>
    [Description("Convert input between timezones.")]
    ConvertTz
}

/// <summary>
/// <see cref="DateTimeOperation.Add"/> 运算的增减单位。
/// </summary>
public enum DateTimeUnit
{
    /// <summary>秒。</summary>
    [Description("Seconds.")]
    Second,

    /// <summary>分钟。</summary>
    [Description("Minutes.")]
    Minute,

    /// <summary>小时。</summary>
    [Description("Hours.")]
    Hour,

    /// <summary>天。</summary>
    [Description("Days.")]
    Day,

    /// <summary>月。</summary>
    [Description("Months.")]
    Month,

    /// <summary>年。</summary>
    [Description("Years.")]
    Year
}

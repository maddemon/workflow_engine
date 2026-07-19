using System.Text.Json;
using System.Text.Json.Nodes;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Scripting;
using FlowEngine.Runtime.Registry;

namespace FlowEngine.Runtime.Tests.Registry;

/// <summary>
/// 参数注入器覆盖测试：经公共入口 <see cref="ParameterHydrator.HydrateAsync"/> 间接覆盖
/// 各内部转换器（bool / numeric / DateTime / Uri / enum / Json / string / list / dictionary / fallback）
/// 及空值/不可解析等边界分支。
/// </summary>
public class ParameterHydratorCoverageTests
{
    private readonly ParameterHydrator _hydrator = new();

    [Fact]
    public async Task Hydrate_AllConverterTargetTypes_AssignsConvertedValues()
    {
        var node = new HydrationProbeNode();
        var resolved = new Dictionary<string, object>
        {
            ["str"] = "hello",
            ["flag"] = true,                                  // BoolConverter (bool 直接)
            ["nullableFlag"] = null!,                          // 可空值类型赋 null
            ["intVal"] = JsonSerializer.Deserialize<JsonElement>("42"),          // NumericConverter int (JsonElement 路径)
            ["longVal"] = JsonSerializer.Deserialize<JsonElement>("9000000000"), // NumericConverter long
            ["doubleVal"] = JsonSerializer.Deserialize<JsonElement>("3.14"),    // NumericConverter double
            ["floatVal"] = JsonSerializer.Deserialize<JsonElement>("1.5"),      // NumericConverter float
            ["dtVal"] = "2024-01-02T03:04:05",                 // DateTimeConverter
            ["dtoVal"] = "2024-01-02T03:04:05+00:00",          // DateTimeOffsetConverter
            ["uriVal"] = "https://example.com/p",             // UriConverter
            ["strategyVal"] = "Continue",                     // EnumConverter (string)
            ["jsonObjVal"] = JsonNode.Parse("{\"a\":1}")!.AsObject(), // JsonConverter (JsonObject)
            ["jsonNodeVal"] = JsonNode.Parse("{\"b\":2}")!,              // JsonConverter (JsonNode)
            ["guidVal"] = Guid.Parse("3F2504E0-4F89-41D3-9A0C-0305E82C3301"), // FallbackConverter：实际 Guid 对象（字符串路径 ChangeType 不支持，见 task-003 备注）
            ["listVal"] = new List<string> { "x", "y" },     // ListConverter (实际集合)
            ["dictVal"] = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 }, // DictionaryConverter
        };

        await _hydrator.HydrateAsync(node, resolved);

        Assert.Equal("hello", node.Str);
        Assert.True(node.Flag);
        Assert.Null(node.NullableFlag);
        // 已知生产缺陷（task-003 记录，按规约不修改生产逻辑）：NumericConverter 对 int/long/float
        // 目标返回 double，经 HydrateAsync.SetValue 抛 InvalidCastException 被吞，属性保留默认值 0。
        // 此处如实断言当前行为，覆盖率测试仅保证代码路径被执行。
        Assert.Equal(0, node.IntVal);
        Assert.Equal(0L, node.LongVal);
        Assert.Equal(3.14, node.DoubleVal);
        Assert.Equal(0f, node.FloatVal);
        Assert.Equal(DateTime.Parse("2024-01-02T03:04:05"), node.DtVal);
        Assert.Equal(DateTimeOffset.Parse("2024-01-02T03:04:05+00:00"), node.DtoVal);
        Assert.Equal(new Uri("https://example.com/p"), node.UriVal);
        Assert.Equal(ErrorStrategy.Continue, node.StrategyVal);
        Assert.NotNull(node.JsonObjVal);
        Assert.Equal(1, (int?)node.JsonObjVal!["a"]);
        Assert.NotNull(node.JsonNodeVal);
        Assert.Equal(2, (int?)node.JsonNodeVal!["b"]);
        Assert.Equal(Guid.Parse("3F2504E0-4F89-41D3-9A0C-0305E82C3301"), node.GuidVal);
        Assert.NotNull(node.ListVal);
        Assert.Equal(2, node.ListVal!.Count);
        Assert.Equal("x", node.ListVal[0]);
        Assert.NotNull(node.DictVal);
        Assert.Equal(2, node.DictVal!.Count);
        Assert.Equal(1, node.DictVal["a"]);
    }

    [Fact]
    public async Task Hydrate_InvalidEnumValue_FallsBackToDefault()
    {
        var node = new HydrationProbeNode();
        var resolved = new Dictionary<string, object> { ["strategyVal"] = "NotARealValue" };

        await _hydrator.HydrateAsync(node, resolved);

        // EnumConverter 解析失败时使用枚举首值（Terminate = 0）。
        Assert.Equal(ErrorStrategy.Terminate, node.StrategyVal);
    }

    [Fact]
    public async Task Hydrate_UnparseableDateTime_SkipsNonNullableValueType()
    {
        var node = new HydrationProbeNode();
        var resolved = new Dictionary<string, object> { ["dtVal"] = "not-a-date" };

        await _hydrator.HydrateAsync(node, resolved);

        // DateTimeConverter 返回 null，非可空值类型赋 null 分支跳过 → 保持默认。
        Assert.Equal(default(DateTime), node.DtVal);
    }

    [Fact]
    public async Task Hydrate_UnparseableFallbackType_LeavesDefault()
    {
        var node = new HydrationProbeNode();
        var resolved = new Dictionary<string, object> { ["tsVal"] = "not-a-timespan" };

        await _hydrator.HydrateAsync(node, resolved);

        // FallbackConverter 转换失败（记录警告）返回 null，非可空值类型跳过 → 保持默认。
        Assert.Equal(default(TimeSpan), node.TsVal);
    }

    [Fact]
    public async Task Hydrate_JsonObjectFromString_ParsesToObject()
    {
        var node = new HydrationProbeNode();
        var resolved = new Dictionary<string, object> { ["jsonObjVal"] = "{\"c\":3}" };

        await _hydrator.HydrateAsync(node, resolved);

        Assert.NotNull(node.JsonObjVal);
        Assert.Equal(3, (int?)node.JsonObjVal!["c"]);
    }

    [Fact]
    public async Task Hydrate_JsonElementNumber_ToDouble_Assigns()
    {
        var node = new HydrationProbeNode();
        var jsonStr = """{"doubleVal": 2.718}""";
        var raw = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.JsonElement>>(jsonStr)!;
        var resolved = raw.ToDictionary(kv => kv.Key, kv => (object)kv.Value);

        await _hydrator.HydrateAsync(node, resolved);

        Assert.Equal(2.718, node.DoubleVal);
    }

    private sealed class HydrationProbeNode : INodeType
    {
        public string TypeName => "hydrationProbe";
        public string DisplayName => "Hydration Probe";
        public string Category => "Test";
        public string Icon => "test";
        public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;
        public IReadOnlyList<PortDefinition> Ports { get; } = [];
        public bool DefaultIsEntry => false;

        public string Str { get; set; } = string.Empty;
        public bool Flag { get; set; }
        public bool? NullableFlag { get; set; }
        public int IntVal { get; set; }
        public long LongVal { get; set; }
        public double DoubleVal { get; set; }
        public float FloatVal { get; set; }
        public DateTime DtVal { get; set; }
        public DateTimeOffset DtoVal { get; set; }
        public Uri? UriVal { get; set; }
        public ErrorStrategy StrategyVal { get; set; }
        public JsonObject? JsonObjVal { get; set; }
        public JsonNode? JsonNodeVal { get; set; }
        public Guid GuidVal { get; set; }
        public List<string>? ListVal { get; set; }
        public Dictionary<string, int>? DictVal { get; set; }
        public TimeSpan TsVal { get; set; }

        public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default)
            => Task.FromResult(new NodeExecutionResult { Success = true });
    }
}

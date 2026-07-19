using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Scripting;
using FlowEngine.Runtime.Registry;
using FlowEngine.Runtime.Registry.Converters;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FlowEngine.Runtime.Tests.Registry.Converters;

/// <summary>
/// 内部值转换器单元测试。各转换器通过 <c>InternalsVisibleTo</c> 对测试程序集可见，
/// 直接覆盖其所有 <c>CanConv</c> / <c>ConvertAsync</c> 分支（含 JsonElement / JsonNode / 异常兜底）。
/// </summary>
public class ConverterUnitTests
{
    private static readonly ParameterHydratorContext Ctx = new(null, NullLogger<ParameterHydrator>.Instance);
    private static readonly ParameterHydratorContext CtxWithLogger = new(null, NullLogger<ParameterHydrator>.Instance);

    private static JsonElement Je(string json) => JsonSerializer.Deserialize<JsonElement>(json);

    private static async Task<T> Conv<T>(IValueConverter converter, object? value, Type targetType, ParameterHydratorContext? ctx = null)
        => (T)(await converter.ConvertAsync(value, targetType, ctx ?? Ctx))!;

    #region BoolConverter

    [Fact]
    public void BoolConverter_CanConvert_BoolOnly()
    {
        var c = new BoolConverter();
        Assert.True(c.CanConvert(typeof(bool)));
        Assert.False(c.CanConvert(typeof(int)));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task BoolConverter_FromBool_ReturnsValue(bool input, bool expected)
        => Assert.Equal(expected, await Conv<bool>(new BoolConverter(), input, typeof(bool)));

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("1", true)]
    [InlineData("0", false)]
    [InlineData("yes", true)]
    public async Task BoolConverter_FromString_ReturnsValue(string input, bool expected)
        => Assert.Equal(expected, await Conv<bool>(new BoolConverter(), input, typeof(bool)));

    [Theory]
    [InlineData(1, true)]
    [InlineData(0, false)]
    [InlineData(5L, true)]
    [InlineData(0L, false)]
    [InlineData(2.5, true)]
    [InlineData(0.0, false)]
    public async Task BoolConverter_FromNumeric_ReturnsValue(object input, bool expected)
        => Assert.Equal(expected, await Conv<bool>(new BoolConverter(), input, typeof(bool)));

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("1", true)]
    [InlineData("0", false)]
    [InlineData("null", false)]
    [InlineData("5", true)]
    public async Task BoolConverter_FromJsonElement_ReturnsValue(string json, bool expected)
        => Assert.Equal(expected, await Conv<bool>(new BoolConverter(), Je(json), typeof(bool)));

    [Fact]
    public async Task BoolConverter_FromUnsupported_ReturnsFalse()
        => Assert.False(await Conv<bool>(new BoolConverter(), new object(), typeof(bool)));

    #endregion

    #region NumericConverter

    // 已知生产缺陷（task-003 记录，按规约不修改生产逻辑）：NumericConverter.ConvertAsync 的三元表达式
    // `int ? long ? double : float` 会将被选择臂统一提升为 double，故 int/long/float 目标实际返回
    // 装箱的 Double 而非目标类型。此处以 object 读取并按数值比较，覆盖各 switch 分支而不依赖装箱类型。
    private static async Task<double> ConvertNum(IValueConverter c, object? v, Type t)
        => System.Convert.ToDouble(await Conv<object>(c, v, t));

    [Fact]
    public void NumericConverter_CanConvert_NumericOnly()
    {
        var c = new NumericConverter();
        Assert.True(c.CanConvert(typeof(int)));
        Assert.True(c.CanConvert(typeof(long)));
        Assert.True(c.CanConvert(typeof(double)));
        Assert.True(c.CanConvert(typeof(float)));
        Assert.False(c.CanConvert(typeof(string)));
        Assert.False(c.CanConvert(typeof(bool)));
    }

    [Fact]
    public async Task NumericConverter_ToInt_FromJsonElementNumber_ReturnsValue()
        => Assert.Equal(42.0, await ConvertNum(new NumericConverter(), Je("42"), typeof(int)), precision: 6);

    [Fact]
    public async Task NumericConverter_ToInt_FromString_ReturnsParsedOrZero()
    {
        var c = new NumericConverter();
        Assert.Equal(7.0, await ConvertNum(c, "7", typeof(int)), precision: 6);
        Assert.Equal(0.0, await ConvertNum(c, "notanint", typeof(int)), precision: 6);
    }

    [Fact]
    public async Task NumericConverter_ToInt_FromLongDoubleFloat_ClampsAndConverts()
    {
        var c = new NumericConverter();
        Assert.Equal(5.0, await ConvertNum(c, 5L, typeof(int)), precision: 6);
        Assert.Equal(5.0, await ConvertNum(c, 5.0, typeof(int)), precision: 6);
        Assert.Equal(5.0, await ConvertNum(c, 5f, typeof(int)), precision: 6);
        Assert.Equal((double)int.MaxValue, await ConvertNum(c, (long)int.MaxValue + 10L, typeof(int)), precision: 0);
        Assert.Equal((double)int.MaxValue, await ConvertNum(c, 1e12, typeof(int)), precision: 0);
    }

    [Fact]
    public async Task NumericConverter_ToLong_FromVariants()
    {
        var c = new NumericConverter();
        Assert.Equal(9.0, await ConvertNum(c, 9, typeof(long)), precision: 6);
        Assert.Equal(9.0, await ConvertNum(c, 9.0, typeof(long)), precision: 6);
        Assert.Equal(9.0, await ConvertNum(c, "9", typeof(long)), precision: 6);
        Assert.Equal(9.0, await ConvertNum(c, Je("9"), typeof(long)), precision: 6);
        Assert.Equal(0.0, await ConvertNum(c, "x", typeof(long)), precision: 6);
    }

    [Fact]
    public async Task NumericConverter_ToDouble_FromVariants()
    {
        var c = new NumericConverter();
        Assert.Equal(3.5, await ConvertNum(c, 3.5, typeof(double)), precision: 6);
        Assert.Equal(3.0, await ConvertNum(c, 3, typeof(double)), precision: 6);
        Assert.Equal(4.0, await ConvertNum(c, 4L, typeof(double)), precision: 6);
        Assert.Equal(3.5, await ConvertNum(c, "3.5", typeof(double)), precision: 6);
        Assert.Equal(3.5, await ConvertNum(c, Je("3.5"), typeof(double)), precision: 6);
    }

    [Fact]
    public async Task NumericConverter_ToFloat_FromVariants()
    {
        var c = new NumericConverter();
        Assert.Equal(1.5, await ConvertNum(c, 1.5f, typeof(float)), precision: 6);
        Assert.Equal(2.0, await ConvertNum(c, 2.0, typeof(float)), precision: 6);
        Assert.Equal(3.0, await ConvertNum(c, 3, typeof(float)), precision: 6);
        Assert.Equal(1.5, await ConvertNum(c, "1.5", typeof(float)), precision: 6);
        Assert.Equal(1.5, await ConvertNum(c, Je("1.5"), typeof(float)), precision: 6);
    }

    [Fact]
    public async Task NumericConverter_FromUnsupported_FallsThroughConvert()
        => Assert.Equal(65.0, await ConvertNum(new NumericConverter(), 'A', typeof(int)), precision: 6);

    #endregion

    #region StringConverter

    [Fact]
    public void StringConverter_CanConvert_StringOnly()
    {
        var c = new StringConverter();
        Assert.True(c.CanConvert(typeof(string)));
        Assert.False(c.CanConvert(typeof(int)));
    }

    [Fact]
    public async Task StringConverter_FromVariants()
    {
        var c = new StringConverter();
        Assert.Equal("hi", await Conv<string>(c, "hi", typeof(string)));
        Assert.Equal("{\"a\":1}", await Conv<string>(c, JsonNode.Parse("{\"a\":1}")!, typeof(string)));
        Assert.Equal("hello", await Conv<string>(c, Je("\"hello\""), typeof(string)));
        Assert.Equal("42", await Conv<string>(c, Je("42"), typeof(string)));
        Assert.Equal("A", await Conv<string>(c, 'A', typeof(string)));
    }

    #endregion

    #region UriConverter

    [Fact]
    public async Task UriConverter_FromString_ReturnsUri()
        => Assert.Equal(new Uri("https://example.com/p"), await Conv<Uri>(new UriConverter(), "https://example.com/p", typeof(Uri)));

    [Fact]
    public void UriConverter_CanConvert_UriOnly()
    {
        var c = new UriConverter();
        Assert.True(c.CanConvert(typeof(Uri)));
        Assert.False(c.CanConvert(typeof(string)));
    }

    #endregion

    #region DateTimeConverter

    [Fact]
    public async Task DateTimeConverter_FromString_ReturnsTyped()
    {
        var c = new DateTimeConverter();
        Assert.Equal(DateTime.Parse("2024-01-02T03:04:05"), await Conv<DateTime>(c, "2024-01-02T03:04:05", typeof(DateTime)));
        Assert.Equal(DateTimeOffset.Parse("2024-01-02T03:04:05+00:00"), await Conv<DateTimeOffset>(c, "2024-01-02T03:04:05+00:00", typeof(DateTimeOffset)));
    }

    [Fact]
    public async Task DateTimeConverter_Unparseable_ReturnsNull()
        => Assert.Null(await new DateTimeConverter().ConvertAsync("not-a-date", typeof(DateTime), Ctx));

    [Fact]
    public void DateTimeConverter_CanConvert_DateTimeKinds()
    {
        var c = new DateTimeConverter();
        Assert.True(c.CanConvert(typeof(DateTime)));
        Assert.True(c.CanConvert(typeof(DateTimeOffset)));
        Assert.False(c.CanConvert(typeof(string)));
    }

    #endregion

    #region EnumConverter

    [Fact]
    public async Task EnumConverter_FromString_CaseInsensitive()
        => Assert.Equal(ErrorStrategy.Continue, await Conv<ErrorStrategy>(new EnumConverter(), "continue", typeof(ErrorStrategy)));

    [Fact]
    public async Task EnumConverter_FromInt_And_JsonElement()
    {
        var c = new EnumConverter();
        Assert.Equal(ErrorStrategy.Terminate, await Conv<ErrorStrategy>(c, 0, typeof(ErrorStrategy)));
        Assert.Equal(ErrorStrategy.Continue, await Conv<ErrorStrategy>(c, Je("\"Continue\""), typeof(ErrorStrategy)));
        Assert.Equal(ErrorStrategy.Terminate, await Conv<ErrorStrategy>(c, Je("0"), typeof(ErrorStrategy)));
    }

    [Fact]
    public async Task EnumConverter_Invalid_FallsBackToFirstValue()
        => Assert.Equal(ErrorStrategy.Terminate, await Conv<ErrorStrategy>(new EnumConverter(), "NotAValue", typeof(ErrorStrategy), CtxWithLogger));

    [Fact]
    public void EnumConverter_CanConvert_EnumOnly()
    {
        var c = new EnumConverter();
        Assert.True(c.CanConvert(typeof(ErrorStrategy)));
        Assert.False(c.CanConvert(typeof(int)));
    }

    #endregion

    #region ListConverter

    [Fact]
    public void ListConverter_CanConvert_ListAndArray()
    {
        var c = new ListConverter();
        Assert.True(c.CanConvert(typeof(List<int>)));
        Assert.True(c.CanConvert(typeof(int[])));
        Assert.False(c.CanConvert(typeof(string)));
    }

    [Fact]
    public async Task ListConverter_FromJsonElementArray_ReturnsList()
    {
        var r = await Conv<List<int>>(new ListConverter(), Je("[1,2,3]"), typeof(List<int>));
        Assert.Equal(new List<int> { 1, 2, 3 }, r);
    }

    [Fact]
    public async Task ListConverter_FromStringJsonArray_ReturnsList()
    {
        var r = await Conv<List<int>>(new ListConverter(), "[1,2]", typeof(List<int>));
        Assert.Equal(new List<int> { 1, 2 }, r);
    }

    [Fact]
    public async Task ListConverter_FromJsonNodeArray_ReturnsList()
    {
        var r = await Conv<List<int>>(new ListConverter(), JsonNode.Parse("[9]")!, typeof(List<int>));
        Assert.Equal(new List<int> { 9 }, r);
    }

    [Fact]
    public async Task ListConverter_FromExistingList_ReturnsSame()
    {
        var src = new List<int> { 4, 5 };
        Assert.Same(src, await Conv<List<int>>(new ListConverter(), src, typeof(List<int>)));
    }

    [Fact]
    public async Task ListConverter_FromArrayTarget_ReturnsArray()
    {
        var r = await Conv<int[]>(new ListConverter(), Je("[1,2]"), typeof(int[]));
        Assert.Equal(new[] { 1, 2 }, r);
    }

    [Fact]
    public async Task ListConverter_MismatchedItemType_ReturnsNull()
        => Assert.Null(await new ListConverter().ConvertAsync(new List<object> { "x" }, typeof(List<int>), CtxWithLogger));

    [Fact]
    public async Task ListConverter_InvalidJson_LogsAndReturnsNull()
        => Assert.Null(await new ListConverter().ConvertAsync("not-json", typeof(List<int>), CtxWithLogger));

    #endregion

    #region DictionaryConverter

    [Fact]
    public async Task DictionaryConverter_FromJsonElementObject_ReturnsDict()
    {
        var r = await Conv<Dictionary<string, int>>(new DictionaryConverter(), Je("{\"a\":1}"), typeof(Dictionary<string, int>));
        Assert.Equal(1, r["a"]);
    }

    [Fact]
    public async Task DictionaryConverter_FromStringJson_ReturnsDict()
    {
        var r = await Conv<Dictionary<string, int>>(new DictionaryConverter(), "{\"b\":2}", typeof(Dictionary<string, int>));
        Assert.Equal(2, r["b"]);
    }

    [Fact]
    public async Task DictionaryConverter_FromJsonNode_ReturnsDict()
    {
        var r = await Conv<Dictionary<string, int>>(new DictionaryConverter(), JsonNode.Parse("{\"c\":3}")!, typeof(Dictionary<string, int>));
        Assert.Equal(3, r["c"]);
    }

    [Fact]
    public async Task DictionaryConverter_FromUnsupported_ReturnsNull()
        => Assert.Null(await new DictionaryConverter().ConvertAsync(123, typeof(Dictionary<string, int>), Ctx));

    [Fact]
    public async Task DictionaryConverter_InvalidJson_LogsAndReturnsNull()
        => Assert.Null(await new DictionaryConverter().ConvertAsync("{bad", typeof(Dictionary<string, int>), CtxWithLogger));

    #endregion

    #region JsonConverter

    [Fact]
    public async Task JsonConverter_FromJsonObject_ReturnsSame()
    {
        var obj = JsonNode.Parse("{\"a\":1}")!.AsObject();
        Assert.Same(obj, await Conv<JsonObject>(new JsonConverter(), obj, typeof(JsonObject)));
    }

    [Fact]
    public async Task JsonConverter_FromJsonNodeObject_ReturnsAsObject()
    {
        var r = await Conv<JsonObject>(new JsonConverter(), JsonNode.Parse("{\"a\":1}")!, typeof(JsonObject));
        Assert.NotNull(r);
    }

    [Fact]
    public async Task JsonConverter_FromJsonNodeNonObject_ReturnsNull()
        => Assert.Null(await new JsonConverter().ConvertAsync(JsonNode.Parse("5")!, typeof(JsonObject), Ctx));

    [Fact]
    public async Task JsonConverter_FromString_ReturnsJsonNode()
    {
        var r = await Conv<JsonNode>(new JsonConverter(), "{\"a\":1}", typeof(JsonNode));
        Assert.NotNull(r);
        Assert.IsType<JsonObject>(r);
    }

    [Fact]
    public async Task JsonConverter_FromJsonElementObject_ReturnsJsonObject()
        => Assert.IsType<JsonObject>(await Conv<JsonObject>(new JsonConverter(), Je("{\"a\":1}"), typeof(JsonObject)));

    [Fact]
    public async Task JsonConverter_FromJsonElementNonObject_ReturnsNull()
        => Assert.Null(await new JsonConverter().ConvertAsync(Je("5"), typeof(JsonObject), Ctx));

    [Fact]
    public void JsonConverter_CanConvert_JsonTypes()
    {
        var c = new JsonConverter();
        Assert.True(c.CanConvert(typeof(JsonObject)));
        Assert.True(c.CanConvert(typeof(JsonNode)));
        Assert.False(c.CanConvert(typeof(string)));
    }

    #endregion

    #region FallbackConverter

    [Fact]
    public void FallbackConverter_CanConvert_AlwaysTrue()
        => Assert.True(new FallbackConverter().CanConvert(typeof(object)));

    [Fact]
    public async Task FallbackConverter_FromConvertible_ReturnsConverted()
        => Assert.Equal(5, await Conv<int>(new FallbackConverter(), "5", typeof(int)));

    [Fact]
    public async Task FallbackConverter_FromIncompatible_LogsAndReturnsNull()
        => Assert.Null(await new FallbackConverter().ConvertAsync(Je("{\"a\":1}"), typeof(int), CtxWithLogger));

    #endregion

    #region ScriptConverter

    [Fact]
    public void ScriptConverter_CanConvert_ScriptAndDictionary()
    {
        var c = new ScriptConverter();
        Assert.True(c.CanConvert(typeof(Script)));
        Assert.True(c.CanConvert(typeof(Dictionary<string, Script>)));
        Assert.False(c.CanConvert(typeof(Dictionary<int, Script>)));
        Assert.False(c.CanConvert(typeof(string)));
    }

    [Fact]
    public async Task ScriptConverter_FromScript_ReturnsSame()
    {
        var s = new Script { Source = "x" };
        Assert.Same(s, await Conv<Script>(new ScriptConverter(), s, typeof(Script)));
    }

    [Fact]
    public async Task ScriptConverter_FromString_ReturnsScript()
    {
        var r = await Conv<Script>(new ScriptConverter(), "function(){}", typeof(Script));
        Assert.Equal("function(){}", r.Source);
    }

    [Fact]
    public async Task ScriptConverter_FromJsonElementString_ReturnsScript()
    {
        var r = await Conv<Script>(new ScriptConverter(), Je("\"code\""), typeof(Script));
        Assert.Equal("code", r.Source);
    }

    [Fact]
    public async Task ScriptConverter_FromJsonObject_ReturnsScriptDictionary()
    {
        var r = await Conv<Dictionary<string, Script>>(new ScriptConverter(), JsonNode.Parse("{\"k\":\"v\"}")!, typeof(Dictionary<string, Script>));
        Assert.Equal("v", r["k"].Source);
    }

    [Fact]
    public async Task ScriptConverter_FromDictionary_ReturnsDictionary()
    {
        var dict = new Dictionary<string, Script> { ["a"] = new Script { Source = "1" } };
        Assert.Same(dict, await Conv<Dictionary<string, Script>>(new ScriptConverter(), dict, typeof(Dictionary<string, Script>)));
    }

    #endregion

    #region CredentialConverter

    private sealed class FakeCredentialAccessor : ICredentialAccessor
    {
        public CredentialValue? ById { get; set; }
        public CredentialValue? ByName { get; set; }
        public bool Throw { get; set; }

        public Task<CredentialValue> GetCredentialAsync(Guid credentialId, CancellationToken cancellationToken = default)
            => Throw
                ? throw new InvalidOperationException("boom")
                : Task.FromResult(ById ?? new CredentialValue());

        public Task<CredentialValue?> GetCredentialByNameAsync(string name, CancellationToken cancellationToken = default)
            => Throw
                ? throw new InvalidOperationException("boom")
                : Task.FromResult(ByName);
    }

    [Fact]
    public async Task CredentialConverter_NullAccessor_ReturnsNull()
    {
        var ctx = new ParameterHydratorContext(null, NullLogger<ParameterHydrator>.Instance);
        Assert.Null(await new CredentialConverter().ConvertAsync("abc", typeof(CredentialValue), ctx));
    }

    [Fact]
    public async Task CredentialConverter_FromGuidString_ReturnsCredential()
    {
        var id = Guid.NewGuid();
        var cred = new CredentialValue { Name = "db", Type = "api" };
        var ctx = new ParameterHydratorContext(new FakeCredentialAccessor { ById = cred }, NullLogger<ParameterHydrator>.Instance);
        var r = await Conv<CredentialValue>(new CredentialConverter(), id.ToString(), typeof(CredentialValue), ctx);
        Assert.Equal("db", r.Name);
    }

    [Fact]
    public async Task CredentialConverter_FromNameString_ReturnsCredential()
    {
        var cred = new CredentialValue { Name = "named" };
        var ctx = new ParameterHydratorContext(new FakeCredentialAccessor { ByName = cred }, NullLogger<ParameterHydrator>.Instance);
        var r = await Conv<CredentialValue>(new CredentialConverter(), "named", typeof(CredentialValue), ctx);
        Assert.Equal("named", r.Name);
    }

    [Fact]
    public async Task CredentialConverter_AccessorThrows_ReturnsNull()
    {
        var ctx = new ParameterHydratorContext(new FakeCredentialAccessor { Throw = true }, NullLogger<ParameterHydrator>.Instance);
        Assert.Null(await new CredentialConverter().ConvertAsync(Guid.NewGuid().ToString(), typeof(CredentialValue), ctx));
    }

    [Fact]
    public async Task CredentialConverter_NonStringValue_ReturnsNull()
        => Assert.Null(await new CredentialConverter().ConvertAsync(123, typeof(CredentialValue), Ctx));

    #endregion
}

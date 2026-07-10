using FlowEngine.Core.Exceptions;
using FlowEngine.Core.Scripting;
using Microsoft.Extensions.Options;

namespace FlowEngine.Core.Tests.Scripting;

public sealed class ScriptCacheTests
{
    private static ScriptCache CreateCache(JsEngineOptions? options = null)
    {
        return new ScriptCache(Options.Create(options ?? new JsEngineOptions()));
    }

    [Fact]
    public void GetOrPrepare_SameSource_ReturnsSameCacheKey()
    {
        var cache = CreateCache();
        var a = new Script { Source = "1 + 1" };
        var b = new Script { Source = "1 + 1", ReturnType = ScriptReturnType.Number };

        var preparedA = cache.GetOrPrepare(a);
        var preparedB = cache.GetOrPrepare(b);

        Assert.Equal(preparedA.CacheKey, preparedB.CacheKey);
    }

    [Fact]
    public void GetOrPrepare_DifferentSources_ReturnsDifferentCacheKeys()
    {
        var cache = CreateCache();
        var a = new Script { Source = "1 + 1" };
        var b = new Script { Source = "2 + 2" };

        var preparedA = cache.GetOrPrepare(a);
        var preparedB = cache.GetOrPrepare(b);

        Assert.NotEqual(preparedA.CacheKey, preparedB.CacheKey);
    }

    [Fact]
    public void GetOrPrepare_ForbiddenIdentifier_ThrowsScriptErrorException()
    {
        var cache = CreateCache();
        var script = new Script { Source = "eval('1')" };

        Assert.Throws<ScriptErrorException>(() => cache.GetOrPrepare(script));
    }

    [Fact]
    public void GetOrPrepare_ForbiddenIdentifierInString_IsAllowed()
    {
        var cache = CreateCache();
        var script = new Script { Source = "'eval is safe here'" };

        var prepared = cache.GetOrPrepare(script);

        Assert.NotNull(prepared);
    }

    [Fact]
    public void TrimIfNeeded_RemovesOldestEntries()
    {
        var cache = CreateCache();
        var scripts = new[] { "1", "2", "3", "4" }.Select(s => new Script { Source = s }).ToArray();

        foreach (var script in scripts)
        {
            cache.GetOrPrepare(script);
        }

        cache.TrimIfNeeded(2);

        // 仅保留最新的两条。
        Assert.Equal(2, GetInsertionOrderCount(cache));
        Assert.Equal(2, GetCacheCount(cache));

        // 重新获取被移除的最旧条目应再次加入缓存。
        cache.GetOrPrepare(scripts[0]);
        Assert.Equal(3, GetInsertionOrderCount(cache));
        Assert.Equal(3, GetCacheCount(cache));

        // 重新获取仍存在的条目不应重复加入。
        cache.GetOrPrepare(scripts[3]);
        Assert.Equal(3, GetInsertionOrderCount(cache));
        Assert.Equal(3, GetCacheCount(cache));
    }

    private static int GetInsertionOrderCount(ScriptCache cache)
    {
        var field = typeof(ScriptCache).GetField("_insertionOrder", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var list = (System.Collections.IList?)field?.GetValue(cache);
        return list?.Count ?? 0;
    }

    private static int GetCacheCount(ScriptCache cache)
    {
        var field = typeof(ScriptCache).GetField("_cache", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var dictionary = (System.Collections.IDictionary?)field?.GetValue(cache);
        return dictionary?.Count ?? 0;
    }

    [Fact]
    public void GetOrPrepare_ForbiddenIdentifierInRegexLiteral_IsAllowed()
    {
        var cache = CreateCache();
        var script = new Script { Source = "/eval/gi.test('hello')" };

        var prepared = cache.GetOrPrepare(script);

        Assert.NotNull(prepared);
    }

    [Fact]
    public void GetOrPrepare_ForbiddenIdentifierInRegexLiteralAfterLineComment_IsAllowed()
    {
        var cache = CreateCache();
        var script = new Script { Source = "// comment\n/eval/gi.test('hello')" };

        var prepared = cache.GetOrPrepare(script);

        Assert.NotNull(prepared);
    }

    [Fact]
    public void GetOrPrepare_ForbiddenIdentifierInRegexLiteralAfterBlockComment_IsAllowed()
    {
        var cache = CreateCache();
        var script = new Script { Source = "/* comment */ /eval/gi.test('hello')" };

        var prepared = cache.GetOrPrepare(script);

        Assert.NotNull(prepared);
    }

}

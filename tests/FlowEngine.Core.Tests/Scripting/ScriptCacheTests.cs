using FlowEngine.Core.Exceptions;
using FlowEngine.Core.Scripting;
using FlowEngine.Core.Scripting.Models;
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

        var firstAgain = cache.GetOrPrepare(scripts[0]);
        var lastAgain = cache.GetOrPrepare(scripts[3]);

        Assert.NotNull(firstAgain);
        Assert.NotNull(lastAgain);
    }

}

using FlowEngine.Core.DependencyInjection;
using FlowEngine.Core.Scripting;
using Microsoft.Extensions.DependencyInjection;

namespace FlowEngine.Core.Tests.DependencyInjection;

public sealed class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddFlowEngineCoreScripting_RegistersScriptCacheAsSingleton()
    {
        var services = new ServiceCollection();
        services.AddFlowEngineCoreScripting();

        var provider = services.BuildServiceProvider();
        var cache1 = provider.GetRequiredService<ScriptCache>();
        var cache2 = provider.GetRequiredService<ScriptCache>();

        Assert.IsType<ScriptCache>(cache1);
        Assert.Same(cache1, cache2);
    }

    [Fact]
    public void AddFlowEngineCoreScripting_RegistersJsEngineOptions()
    {
        var services = new ServiceCollection();
        services.AddFlowEngineCoreScripting();

        var provider = services.BuildServiceProvider();
        var options = provider.GetService<JsEngineOptions>();

        Assert.NotNull(options);
        Assert.NotEmpty(options.ForbiddenIdentifiers);
    }
}

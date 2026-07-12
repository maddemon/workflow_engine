using FlowEngine.Core.Scripting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FlowEngine.Core.DependencyInjection;

/// <summary>
/// FlowEngine.Core 服务注册扩展。
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 注册 FlowEngine.Core 的脚本基础设施。
    /// </summary>
    public static IServiceCollection AddFlowEngineCoreScripting(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<JsEngineOptions>();
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<JsEngineOptions>>().Value);
        services.AddSingleton<ScriptCache>();

        return services;
    }
}

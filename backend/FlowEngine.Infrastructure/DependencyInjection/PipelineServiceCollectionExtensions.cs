using FlowEngine.Core.Abstractions;
using FlowEngine.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FlowEngine.Infrastructure.DependencyInjection;

/// <summary>
/// Phase 3 节点执行管线相关的独立 DI 服务注册：将节点对基础设施的直接依赖
/// （凭据、共享内存、递归保护、HTTP、子执行）抽象为接口，便于 Phase 4 节点迁移与测试替换。
/// </summary>
public static class PipelineServiceCollectionExtensions
{
    /// <summary>注册节点执行相关的抽象服务。</summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configuration">配置（用于读取 <see cref="RecursionGuardOptions"/>）。</param>
    /// <returns>同一服务集合。</returns>
    public static IServiceCollection AddPipelineServices(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<ICredentialService, NodeCredentialService>();
        services.AddScoped<IWorkflowMemoryService, WorkflowMemoryService>();
        services.AddScoped<IRecursionGuard>(sp =>
        {
            var options = configuration.GetSection(RecursionGuardOptions.SectionName).Get<RecursionGuardOptions>()
                ?? new RecursionGuardOptions();
            return new RecursionGuard(options);
        });
        services.AddSingleton<IHttpExecutionService, HttpExecutionServiceAdapter>();
        services.AddScoped<ISubExecutionService, SubExecutionService>();

        return services;
    }
}

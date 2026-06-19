using System.Text.Json.Serialization;
using FlowEngine.Application.Credentials;
using FlowEngine.Application.Executions;
using FlowEngine.Application.Workflows;
using FlowEngine.Core.Abstractions;
using FlowEngine.Infrastructure.Persistence;
using FlowEngine.Infrastructure.Persistence.Repositories;
using FlowEngine.Infrastructure.Security;
using FlowEngine.Runtime.Credentials;
using FlowEngine.Runtime.Expressions;
using FlowEngine.Runtime.Executor;
using FlowEngine.Runtime.Registry;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    });
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<ExpressionEvaluator>();
builder.Services.AddScoped<ParameterResolver>();

builder.Services.AddDbContext<FlowEngineDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddScoped<IWorkflowRepository, WorkflowRepository>();
builder.Services.AddScoped<IExecutionStore, ExecutionStore>();
builder.Services.AddScoped<ICredentialRepository, CredentialRepository>();
builder.Services.AddSingleton<ICryptoKeyProvider, CryptoKeyProvider>();
builder.Services.AddSingleton<ICredentialEncryptionService, CredentialEncryptionService>();
builder.Services.AddScoped<CredentialService>();
builder.Services.AddScoped<ICredentialAccessor, CredentialAccessor>();
builder.Services.AddScoped<WorkflowValidator>();
builder.Services.AddScoped<WorkflowService>();
builder.Services.AddScoped<ExecutionService>();
builder.Services.AddScoped<NodeExecutionContextFactory>(provider =>
{
    var whitelist = builder.Configuration.GetSection("Expression:EnvironmentWhitelist").Get<string[]>() ?? [];
    return new NodeExecutionContextFactory(
        provider.GetRequiredService<INodeRegistry>(),
        provider.GetRequiredService<ExpressionEvaluator>(),
        provider.GetRequiredService<ParameterResolver>(),
        provider.GetRequiredService<ICredentialAccessor>(),
        new HashSet<string>(whitelist, StringComparer.OrdinalIgnoreCase),
        provider.GetService<ILogger<ParameterHydrator>>());
});
builder.Services.AddScoped<ErrorStrategyHandler>();
builder.Services.AddScoped<IEngine, WorkflowExecutor>();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
        if (origins.Length > 0)
        {
            policy.WithOrigins(origins)
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials();
        }
        else
        {
            policy.AllowAnyOrigin()
                  .AllowAnyHeader()
                  .AllowAnyMethod();
        }
    });
});

var pluginsPath = builder.Configuration.GetSection("Plugins")["Path"] ?? "../../plugins";
if (!Path.IsPathRooted(pluginsPath))
{
    pluginsPath = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, pluginsPath));
}

builder.Services.AddSingleton<PluginLoader>(_ =>
    new PluginLoader(pluginsPath, _.GetRequiredService<ILogger<PluginLoader>>()));

builder.Services.AddSingleton<INodeRegistry>(provider =>
{
    var loader = provider.GetRequiredService<PluginLoader>();
    var nodes = loader.LoadNodes();
    var logger = provider.GetRequiredService<ILogger<NodeRegistry>>();
    var registry = new NodeRegistry(nodes, logger);

    logger.LogInformation(
        "节点注册中心初始化完成，已注册 {Count} 个节点类型。",
        registry.GetDescriptors().Count);

    return registry;
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<FlowEngineDbContext>();
    dbContext.Database.Migrate();
    dbContext.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
}

app.UseCors();
app.UseStaticFiles();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
app.MapGet("/health/ready", () => Results.Ok(new { status = "ready" }));

var api = app.MapGroup("/api/v1");
api.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.MapControllers();

app.MapFallbackToFile("index.html");

app.Run();

using FlowEngine.Host;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddFlowEngine(builder.Configuration, builder.Environment);

var app = builder.Build();
await app.UseFlowEngineAsync();

app.Run();

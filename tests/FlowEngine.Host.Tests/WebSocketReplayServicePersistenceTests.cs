using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Host.WebSocketHandlers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace FlowEngine.Host.Tests;

/// <summary>
/// WebSocketReplayService 持久化回放测试。
/// </summary>
public class WebSocketReplayServicePersistenceTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly FlowEngineDbContext _dbContext;
    private readonly WebSocketReplayService _service;

    public WebSocketReplayServicePersistenceTests()
    {
        var services = new ServiceCollection();
        services.AddDbContext<FlowEngineDbContext>(options =>
            options.UseSqlite("DataSource=:memory:"), ServiceLifetime.Singleton);
        services.AddSingleton<WebSocketReplayService>(provider =>
            new WebSocketReplayService(
                Mock.Of<ILogger<WebSocketReplayService>>(),
                provider.GetRequiredService<IServiceScopeFactory>()));

        _serviceProvider = services.BuildServiceProvider();
        _dbContext = _serviceProvider.GetRequiredService<FlowEngineDbContext>();
        _dbContext.Database.OpenConnection();
        _dbContext.Database.EnsureCreated();
        _service = _serviceProvider.GetRequiredService<WebSocketReplayService>();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _service.Dispose();
        _dbContext.Database.CloseConnection();
        _dbContext.Dispose();
        _serviceProvider.Dispose();
    }

    [Fact]
    public async Task GetPersistedEventsAsync_NoRecord_ReturnsEmpty()
    {
        var events = await _service.GetPersistedEventsAsync(Guid.NewGuid(), 0, CancellationToken.None);

        Assert.Empty(events);
    }

    [Fact]
    public async Task GetPersistedEventsAsync_RebuildsEventsAndFiltersByLastSequence()
    {
        var executionId = Guid.NewGuid();
        var workflowId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();

        var record = new ExecutionRecord
        {
            Id = executionId,
            WorkflowDefinitionId = workflowId,
            StartedAt = DateTime.UtcNow,
            Status = ExecutionStatus.Completed,
            CompletedAt = DateTime.UtcNow,
            NodeRecords =
            [
                new NodeExecutionRecord
                {
                    Id = Guid.NewGuid(),
                    NodeDefinitionId = nodeId,
                    RunIndex = 0,
                    StartedAt = DateTime.UtcNow,
                    CompletedAt = DateTime.UtcNow,
                    Output = new NodeExecutionResult
                    {
                        Success = true,
                        Output = new DataBatch { Items = [new DataItem { Data = null, Success = true, SourceIndex = 0 }] },
                    },
                }
            ],
        };

        _dbContext.ExecutionRecords.Add(record);
        await _dbContext.SaveChangesAsync(CancellationToken.None);

        var events = await _service.GetPersistedEventsAsync(executionId, 1, CancellationToken.None);

        Assert.Equal(2, events.Count);
        Assert.Equal(2, events[0].Sequence);
        Assert.Equal(3, events[1].Sequence);
    }
}

#pragma warning disable xUnit1051 // Use TestContext.Current.CancellationToken

using FlowEngine.Application.Workflows;
using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FlowEngine.Application.Tests.Workflows;

/// <summary>
/// WorkflowRepository 查询测试。
/// </summary>
public sealed class WorkflowRepositoryTests : IDisposable
{
    private readonly FlowEngineDbContext _dbContext;
    private readonly WorkflowRepository _repository;

    public WorkflowRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<FlowEngineDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _dbContext = new FlowEngineDbContext(options);
        _repository = new WorkflowRepository(_dbContext);
    }

    public void Dispose() => _dbContext.Dispose();

    /// <summary>
    /// 验证无引用时返回空列表。
    /// 注：当 Parameters 经 EF Core JSON 列反序列化后，值实际为 JsonElement，
    /// 当前生产实现仅判断 string，存在已知缺陷（计划禁止改生产逻辑，仅记录）。
    /// </summary>
    [Fact]
    public async Task FindReferencingCredentialAsync_NoReference_ReturnsEmpty()
    {
        var credentialId = Guid.NewGuid();
        _dbContext.Workflows.Add(new Workflow
        {
            Name = "No Reference",
            Nodes =
            [
                new NodeDefinition
                {
                    Id = "n1",
                    TypeName = "fetch",
                    Name = "Fetch",
                    Parameters = new Dictionary<string, object> { ["token"] = "other-value" },
                },
            ],
            Connections = [],
        });
        await _dbContext.SaveChangesAsync();

        var result = await _repository.FindReferencingCredentialAsync(credentialId);

        Assert.Empty(result);
    }
}

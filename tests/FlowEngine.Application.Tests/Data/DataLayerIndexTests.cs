using FlowEngine.Core.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FlowEngine.Application.Tests.Data;

/// <summary>
/// D-2/D-3/D-4/D-13：验证新增的热查询索引在 SQLite 中实际创建。
/// 注：PostgreSQL 等价索引由独立迁移提供，列为 CI 后续覆盖（本测试验证 SQLite 模型正确）。
/// </summary>
public sealed class DataLayerIndexTests : IDisposable
{
    private readonly FlowEngineDbContext _dbContext;

    public DataLayerIndexTests()
    {
        var options = new DbContextOptionsBuilder<FlowEngineDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        _dbContext = new FlowEngineDbContext(options);
        _dbContext.Database.OpenConnection();
        _dbContext.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _dbContext.Database.CloseConnection();
        _dbContext.Dispose();
    }

    private async Task<HashSet<string>> GetIndexNamesAsync()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var conn = (SqliteConnection)_dbContext.Database.GetDbConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT name FROM sqlite_master
            WHERE type = 'index'
              AND name IN (
                'IX_triggers_workflow_definition_id',
                'IX_triggers_project_id',
                'IX_stored_files_project_id',
                'IX_execution_records_workflow_definition_id_started_at')
            """;
        await using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    [Fact]
    public async Task HotQueryIndexes_AreCreated()
    {
        var indexes = await GetIndexNamesAsync();

        Assert.Contains("IX_triggers_workflow_definition_id", indexes); // D-2
        Assert.Contains("IX_triggers_project_id", indexes);             // D-3
        Assert.Contains("IX_stored_files_project_id", indexes);         // D-4
        Assert.Contains("IX_execution_records_workflow_definition_id_started_at", indexes); // D-13 复合索引
    }
}

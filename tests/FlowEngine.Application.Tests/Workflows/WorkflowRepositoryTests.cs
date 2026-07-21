#pragma warning disable xUnit1051 // Use TestContext.Current.CancellationToken

using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Text.Json;
using System.Threading;
using FlowEngine.Application.Workflows;
using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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

    [Fact]
    public async Task FindReferencingCredentialAsync_StringParameterReference_ReturnsWorkflowName()
    {
        var credentialId = Guid.NewGuid();
        _dbContext.Workflows.Add(new Workflow
        {
            Name = "String Reference",
            Nodes =
            [
                new NodeDefinition
                {
                    Id = "n1",
                    TypeName = "fetch",
                    Name = "Fetch",
                    Parameters = new Dictionary<string, object> { ["credentialId"] = credentialId.ToString() },
                },
            ],
            Connections = [],
        });
        await _dbContext.SaveChangesAsync();

        var result = await _repository.FindReferencingCredentialAsync(credentialId);

        Assert.Single(result);
        Assert.Equal("String Reference", result[0]);
    }

    [Fact]
    public async Task FindReferencingCredentialAsync_JsonElementParameterReference_ReturnsWorkflowName()
    {
        var credentialId = Guid.NewGuid();
        var element = JsonSerializer.Deserialize<JsonElement>("\u0022" + credentialId.ToString() + "\u0022");
        _dbContext.Workflows.Add(new Workflow
        {
            Name = "JsonElement Reference",
            Nodes =
            [
                new NodeDefinition
                {
                    Id = "n1",
                    TypeName = "fetch",
                    Name = "Fetch",
                    Parameters = new Dictionary<string, object> { ["credentialId"] = element },
                },
            ],
            Connections = [],
        });
        await _dbContext.SaveChangesAsync();

        var result = await _repository.FindReferencingCredentialAsync(credentialId);

        Assert.Single(result);
        Assert.Equal("JsonElement Reference", result[0]);
    }

    [Fact]
    public async Task FindReferencingCredentialAsync_OneOfManyWorkflows_ReturnsOnlyReferencingName()
    {
        var credentialId = Guid.NewGuid();

        for (var i = 0; i < 5; i++)
        {
            _dbContext.Workflows.Add(new Workflow
            {
                Name = $"Workflow {i}",
                Nodes =
                [
                    new NodeDefinition
                    {
                        Id = "n1",
                        TypeName = "fetch",
                        Name = "Fetch",
                        Parameters = new Dictionary<string, object> { ["other"] = $"value-{i}" },
                    },
                ],
                Connections = [],
            });
        }

        _dbContext.Workflows.Add(new Workflow
        {
            Name = "Referencer",
            Nodes =
            [
                new NodeDefinition
                {
                    Id = "n1",
                    TypeName = "fetch",
                    Name = "Fetch",
                    Parameters = new Dictionary<string, object> { ["credentialId"] = credentialId.ToString() },
                },
            ],
            Connections = [],
        });
        await _dbContext.SaveChangesAsync();

        var result = await _repository.FindReferencingCredentialAsync(credentialId);

        Assert.Single(result);
        Assert.Equal("Referencer", result[0]);
    }

    [Fact]
    public async Task FindReferencingCredentialAsync_QueriesOnlyUsageTable_NotWorkflows()
    {
        // 使用 SQLite + 命令拦截器，证明查询仅命中 workflow_credential_usages，不扫描 workflows 表。
        var sqlLog = new SqlRecordingInterceptor();
        var dbPath = Path.Combine(Path.GetTempPath(), $"wcu-test-{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<FlowEngineDbContext>()
                .UseSqlite($"Data Source={dbPath};Pooling=false")
                .AddInterceptors(sqlLog)
                .Options;
            await using var dbContext = new FlowEngineDbContext(options);
            await dbContext.Database.EnsureCreatedAsync();
            var repository = new WorkflowRepository(dbContext);

            var credentialId = Guid.NewGuid();
            dbContext.Workflows.Add(new Workflow
            {
                Name = "W",
                Nodes =
                [
                    new NodeDefinition
                    {
                        Id = "n1",
                        TypeName = "fetch",
                        Name = "Fetch",
                        Parameters = new Dictionary<string, object> { ["credentialId"] = credentialId.ToString() },
                    },
                ],
                Connections = [],
            });
            await dbContext.SaveChangesAsync();

            // 仅记录 FindReferencingCredentialAsync 调用期间产生的 SQL。
            sqlLog.Clear();
            var result = await repository.FindReferencingCredentialAsync(credentialId);

            Assert.Single(result);
            Assert.Contains(sqlLog.Commands, c => c.Contains("workflow_credential_usages", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(sqlLog.Commands, c => c.Contains("\"workflows\"", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(sqlLog.Commands, c => c.Contains("workflows", StringComparison.OrdinalIgnoreCase) && !c.Contains("workflow_credential_usages", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try
            {
                if (File.Exists(dbPath))
                {
                    File.Delete(dbPath);
                }
            }
            catch
            {
                // 临时文件清理失败不影响测试结论。
            }
        }
    }

    [Fact]
    public async Task SaveChanges_MaintainsWorkflowCredentialUsages()
    {
        var credentialId = Guid.NewGuid();
        var workflow = new Workflow
        {
            Name = "W",
            Nodes =
            [
                new NodeDefinition
                {
                    Id = "n1",
                    TypeName = "fetch",
                    Name = "Fetch",
                    Parameters = new Dictionary<string, object> { ["credentialId"] = credentialId.ToString() },
                },
            ],
            Connections = [],
        };
        _dbContext.Workflows.Add(workflow);
        await _dbContext.SaveChangesAsync();

        // 引用存在 → 有 1 行
        var rows = await _dbContext.WorkflowCredentialUsages
            .Where(u => u.CredentialId == credentialId)
            .ToListAsync();
        Assert.Single(rows);
        Assert.Equal("W", rows[0].WorkflowName);

        // 移除引用后重新保存 → 旧行消失
        workflow.Nodes =
        [
            new NodeDefinition
            {
                Id = "n1",
                TypeName = "fetch",
                Name = "Fetch",
                Parameters = new Dictionary<string, object> { ["other"] = "x" },
            },
        ];
        await _dbContext.SaveChangesAsync();
        rows = await _dbContext.WorkflowCredentialUsages
            .Where(u => u.WorkflowId == workflow.Id)
            .ToListAsync();
        Assert.Empty(rows);

        // 第二个工作流引用同一凭据 → 行数正确
        var w2 = new Workflow
        {
            Name = "W2",
            Nodes =
            [
                new NodeDefinition
                {
                    Id = "n1",
                    TypeName = "fetch",
                    Name = "Fetch",
                    Parameters = new Dictionary<string, object> { ["credentialId"] = credentialId.ToString() },
                },
            ],
            Connections = [],
        };
        _dbContext.Workflows.Add(w2);
        await _dbContext.SaveChangesAsync();
        rows = await _dbContext.WorkflowCredentialUsages
            .Where(u => u.CredentialId == credentialId)
            .ToListAsync();
        Assert.Single(rows);
        Assert.Equal("W2", rows[0].WorkflowName);
    }

    [Fact]
    public async Task Backfill_PopulatesMissingUsageRows()
    {
        var credentialId = Guid.NewGuid();

        // 模拟迁移前已存在的工作流：插入工作流后清掉其引用行（遗留状态）。
        var workflow = new Workflow
        {
            Name = "Legacy",
            Nodes =
            [
                new NodeDefinition
                {
                    Id = "n1",
                    TypeName = "fetch",
                    Name = "Fetch",
                    Parameters = new Dictionary<string, object> { ["credentialId"] = credentialId.ToString() },
                },
            ],
            Connections = [],
        };
        _dbContext.Workflows.Add(workflow);
        await _dbContext.SaveChangesAsync();

        var existing = await _dbContext.WorkflowCredentialUsages
            .Where(u => u.WorkflowId == workflow.Id)
            .ToListAsync();
        _dbContext.WorkflowCredentialUsages.RemoveRange(existing);
        await _dbContext.SaveChangesAsync();
        Assert.Empty(await _dbContext.WorkflowCredentialUsages
            .Where(u => u.WorkflowId == workflow.Id)
            .ToListAsync());

        // 回填
        var backfill = new WorkflowCredentialUsageBackfill(_dbContext);
        var count = await backfill.BackfillAsync();

        Assert.Equal(1, count);
        var rows = await _dbContext.WorkflowCredentialUsages
            .Where(u => u.CredentialId == credentialId)
            .ToListAsync();
        Assert.Single(rows);
        Assert.Equal("Legacy", rows[0].WorkflowName);
    }

    /// <summary>
    /// 记录 EF 执行的 SQL 命令，用于断言 <see cref="WorkflowRepository.FindReferencingCredentialAsync"/>
    /// 不扫描 workflows 表。
    /// </summary>
    private sealed class SqlRecordingInterceptor : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public void Clear() => Commands.Clear();

        public override ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
        }

        public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData eventData, int result)
        {
            Commands.Add(command.CommandText);
            return base.NonQueryExecuted(command, eventData, result);
        }

        public override ValueTask<int> NonQueryExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return base.NonQueryExecutedAsync(command, eventData, result, cancellationToken);
        }
    }
}

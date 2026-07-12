using System.Text.Json;
using System.Text.Json.Nodes;
using FlowEngine.Application.Audit;
using FlowEngine.Application.Identity;
using FlowEngine.Application.Workflows;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Events;
using FlowEngine.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace FlowEngine.Application.Tests.Workflows;

/// <summary>
/// WorkflowExportService 凭据脱敏测试（GAP-01 / Code Review I-4）。
/// 验证导出 JSON 中不含 CredentialValue 的明文 fields/binaryFields 字段。
/// </summary>
public sealed class WorkflowExportServiceTests : IDisposable
{
    private readonly FlowEngineDbContext _dbContext;
    private readonly WorkflowExportService _service;

    public WorkflowExportServiceTests()
    {
        var options = new DbContextOptionsBuilder<FlowEngineDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbContext = new FlowEngineDbContext(options);
        var eventBus = new InMemoryEventBus();
        var auditFactory = new AuditEventFactory(new FakeUserContext { UserId = Guid.NewGuid() });
        _service = new WorkflowExportService(_dbContext, eventBus, auditFactory);
    }

    public void Dispose() => _dbContext.Dispose();

    [Fact]
    public async Task ExportAsync_NodeWithCredentialFields_RemovesFieldsFromOutput()
    {
        var ct = TestContext.Current.CancellationToken;
        var workflow = CreateWorkflowWithParameters(new Dictionary<string, object>
        {
            ["name"] = "api-key",
            ["type"] = "apiKey",
            // CredentialValue 明文字段，必须被脱敏
            ["fields"] = new Dictionary<string, object>
            {
                ["apiKey"] = "sk-secret-12345",
                ["token"] = "bearer-xxx",
            },
        });

        var json = await _service.ExportAsync(workflow.Id, "tester", ct);
        var nodeParams = ExtractFirstNodeParameters(json);

        Assert.False(nodeParams.ContainsKey("fields"), "导出 JSON 不应包含凭据明文 fields 字段。");
        Assert.True(nodeParams.ContainsKey("name"), "非敏感字段 name 应保留。");
        Assert.True(nodeParams.ContainsKey("type"), "非敏感字段 type 应保留。");
        Assert.DoesNotContain("sk-secret-12345", json);
        Assert.DoesNotContain("bearer-xxx", json);
    }

    [Fact]
    public async Task ExportAsync_NodeWithBinaryFields_RemovesBinaryFieldsFromOutput()
    {
        var ct = TestContext.Current.CancellationToken;
        var workflow = CreateWorkflowWithParameters(new Dictionary<string, object>
        {
            ["name"] = "client-cert",
            ["type"] = "certificate",
            ["binaryFields"] = new Dictionary<string, object>
            {
                ["pfx"] = "base64-encoded-binary-secret",
            },
        });

        var json = await _service.ExportAsync(workflow.Id, "tester", ct);
        var nodeParams = ExtractFirstNodeParameters(json);

        Assert.False(nodeParams.ContainsKey("binaryFields"), "导出 JSON 不应包含凭据明文 binaryFields 字段。");
        Assert.DoesNotContain("base64-encoded-binary-secret", json);
    }

    [Fact]
    public async Task ExportAsync_NestedCredentialValue_RemovesFieldsFromNestedObject()
    {
        var ct = TestContext.Current.CancellationToken;
        var workflow = CreateWorkflowWithParameters(new Dictionary<string, object>
        {
            ["credential"] = new Dictionary<string, object>
            {
                ["name"] = "nested-key",
                ["fields"] = new Dictionary<string, object>
                {
                    ["secret"] = "nested-secret-value",
                },
            },
        });

        var json = await _service.ExportAsync(workflow.Id, "tester", ct);
        var nodeParams = ExtractFirstNodeParameters(json);

        Assert.True(nodeParams.ContainsKey("credential"), "外层 credential 引用应保留。");
        var credential = Assert.IsType<JsonObject>(nodeParams["credential"]);
        Assert.False(credential.ContainsKey("fields"), "嵌套对象内的 fields 也应被脱敏。");
        Assert.True(credential.ContainsKey("name"), "嵌套对象内的非敏感字段应保留。");
        Assert.DoesNotContain("nested-secret-value", json);
    }

    [Fact]
    public async Task ExportAsync_CredentialValueInArray_RemovesFieldsFromArrayItems()
    {
        var ct = TestContext.Current.CancellationToken;
        var workflow = CreateWorkflowWithParameters(new Dictionary<string, object>
        {
            ["credentials"] = new List<object>
            {
                new Dictionary<string, object>
                {
                    ["name"] = "arr-key-1",
                    ["fields"] = new Dictionary<string, object> { ["token"] = "array-secret-1" },
                },
                new Dictionary<string, object>
                {
                    ["name"] = "arr-key-2",
                    ["binaryFields"] = new Dictionary<string, object> { ["data"] = "array-secret-2" },
                },
            },
        });

        var json = await _service.ExportAsync(workflow.Id, "tester", ct);
        var nodeParams = ExtractFirstNodeParameters(json);

        var credentials = Assert.IsType<JsonArray>(nodeParams["credentials"]);
        Assert.Equal(2, credentials.Count);
        var first = Assert.IsType<JsonObject>(credentials[0]!);
        var second = Assert.IsType<JsonObject>(credentials[1]!);
        Assert.False(first.ContainsKey("fields"), "数组项中的 fields 应被脱敏。");
        Assert.False(second.ContainsKey("binaryFields"), "数组项中的 binaryFields 应被脱敏。");
        Assert.True(first.ContainsKey("name"), "数组项中的非敏感字段应保留。");
        Assert.DoesNotContain("array-secret-1", json);
        Assert.DoesNotContain("array-secret-2", json);
    }

    [Fact]
    public async Task ExportAsync_NodeWithoutCredentialFields_PreservesAllParameters()
    {
        var ct = TestContext.Current.CancellationToken;
        var workflow = CreateWorkflowWithParameters(new Dictionary<string, object>
        {
            ["url"] = "https://api.example.com",
            ["method"] = "GET",
            ["timeout"] = 30,
        });

        var json = await _service.ExportAsync(workflow.Id, "tester", ct);
        var nodeParams = ExtractFirstNodeParameters(json);

        Assert.Equal(3, nodeParams.Count);
        Assert.Equal("https://api.example.com", nodeParams["url"]!.GetValue<string>());
        Assert.Equal("GET", nodeParams["method"]!.GetValue<string>());
        Assert.Equal(30, nodeParams["timeout"]!.GetValue<int>());
    }

    [Fact]
    public async Task ExportBatchAsync_AllWorkflowsSanitized()
    {
        var ct = TestContext.Current.CancellationToken;
        var w1 = CreateWorkflowWithParameters(new Dictionary<string, object>
        {
            ["name"] = "w1-cred",
            ["fields"] = new Dictionary<string, object> { ["apiKey"] = "w1-secret" },
        });
        var w2 = CreateWorkflowWithParameters(new Dictionary<string, object>
        {
            ["name"] = "w2-cred",
            ["binaryFields"] = new Dictionary<string, object> { ["pfx"] = "w2-binary" },
        });

        var json = await _service.ExportBatchAsync([w1.Id, w2.Id], "tester", ct);

        Assert.DoesNotContain("w1-secret", json);
        Assert.DoesNotContain("w2-binary", json);
        Assert.DoesNotContain("\"fields\"", json);
        Assert.DoesNotContain("\"binaryFields\"", json);

        // 反序列化验证数组结构正确
        var array = JsonNode.Parse(json)!;
        Assert.Equal(2, array.AsArray().Count);
    }

    [Fact]
    public async Task ExportAsync_NonExistentWorkflow_Throws()
    {
        var ct = TestContext.Current.CancellationToken;
        await Assert.ThrowsAsync<NotFoundException>(
            () => _service.ExportAsync(Guid.NewGuid(), "tester", ct));
    }

    [Fact]
    public async Task ExportBatchAsync_PartialMissingIds_Throws()
    {
        var ct = TestContext.Current.CancellationToken;
        var existing = CreateWorkflowWithParameters([]);

        await Assert.ThrowsAsync<NotFoundException>(
            () => _service.ExportBatchAsync([existing.Id, Guid.NewGuid()], "tester", ct));
    }

    private sealed class FakeUserContext : IUserContext
    {
        public bool IsAuthenticated => UserId.HasValue;
        public Guid? UserId { get; set; }
        public string? Email => "test@test.com";
        public IReadOnlyList<string> Roles { get; set; } = [];
    }

    private sealed class InMemoryEventBus : IEventBus
    {
        public Task PublishAsync<TEvent>(TEvent eventInstance, CancellationToken cancellationToken = default)
            where TEvent : IDomainEvent => Task.CompletedTask;
        public IDisposable Subscribe<TEvent>(Func<TEvent, CancellationToken, Task> handler)
            where TEvent : IDomainEvent => new Disposable();
        private sealed class Disposable : IDisposable { public void Dispose() { } }
    }

    /// <summary>
    /// 构造一个带指定参数的 Workflow 并保存到数据库。
    /// </summary>
    private Workflow CreateWorkflowWithParameters(Dictionary<string, object> parameters)
    {
        var workflow = new Workflow
        {
            ProjectId = Guid.NewGuid(),
            Name = "Export-Test-Workflow",
            CreatedBy = "tester",
            IsActive = true,
            Nodes =
            [
                new NodeDefinition
                {
                    Id = "testNode",
                    TypeName = "testNode",
                    Name = "TestNode",
                    Parameters = parameters,
                },
            ],
            Connections = [],
        };
        _dbContext.Workflows.Add(workflow);
        _dbContext.SaveChanges();
        return workflow;
    }

    /// <summary>
    /// 从导出 JSON 中提取第一个节点的 Parameters（JsonObject 形式）。
    /// </summary>
    private static JsonObject ExtractFirstNodeParameters(string json)
    {
        var root = JsonNode.Parse(json) ?? throw new InvalidOperationException("导出 JSON 解析失败。");
        var nodes = root["nodes"] ?? throw new InvalidOperationException("导出 JSON 缺少 nodes 字段。");
        var firstNode = nodes.AsArray()[0] ?? throw new InvalidOperationException("nodes 数组为空。");
        var parameters = firstNode["parameters"] ?? throw new InvalidOperationException("节点缺少 parameters 字段。");
        return Assert.IsType<JsonObject>(parameters);
    }
}

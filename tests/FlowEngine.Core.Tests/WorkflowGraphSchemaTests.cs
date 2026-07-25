using System.Text.Json.Nodes;
using FlowEngine.Core;

namespace FlowEngine.Core.Tests;

public sealed class WorkflowGraphSchemaTests
{
    private static JsonNode Parse(string json) => JsonNode.Parse(json)!;

    [Fact]
    public void CurrentVersion_IsOne()
    {
        Assert.Equal(1, WorkflowGraphSchema.CurrentVersion);
    }

    [Fact]
    public void ReadVersion_LegacyBareArray_ReturnsCurrent()
    {
        var graph = Parse("[{\"id\":\"n1\"}]");

        Assert.Equal(WorkflowGraphSchema.CurrentVersion, WorkflowGraphSchema.ReadVersion(graph));
    }

    [Fact]
    public void ReadVersion_Envelope_ReadsSchemaVersion()
    {
        var graph = Parse("{\"schemaVersion\":1,\"nodes\":[],\"connections\":[]}");

        Assert.Equal(1, WorkflowGraphSchema.ReadVersion(graph));
    }

    [Fact]
    public void NormalizeArray_LegacyData_NoMigration_ReturnsUnchanged()
    {
        var legacy = Parse("[{\"id\":\"n1\",\"name\":\"x\"},{\"id\":\"n2\"}]");

        var normalized = WorkflowGraphSchema.NormalizeArray(legacy, sourceVersion: 1);

        Assert.Equal(legacy.ToJsonString(), normalized.ToJsonString());
    }

    [Fact]
    public void NormalizeArray_RegisteredMigration_AppliedAndVersionAdvanced()
    {
        // 模拟未来 v1 -> v2 迁移：为每个节点补充默认 "timeout" 字段。
        WorkflowGraphSchema.RegisterMigration(1, g =>
        {
            if (g is JsonArray arr)
            {
                foreach (var node in arr)
                {
                    if (node is JsonObject obj && !obj.ContainsKey("timeout"))
                    {
                        obj["timeout"] = 30;
                    }
                }
            }

            return g;
        });

        var v1 = Parse("[{\"id\":\"n1\"},{\"id\":\"n2\"}]");
        // 模拟未来目标版本提升为 2，触发 v1 -> v2 迁移。
        var normalized = WorkflowGraphSchema.NormalizeArray(v1, sourceVersion: 1, targetVersion: 2);

        var arr = Assert.IsType<JsonArray>(normalized);
        Assert.Equal(2, arr.Count);
        foreach (var node in arr)
        {
            Assert.Equal(30, node!["timeout"]!.GetValue<int>());
        }
    }

    [Fact]
    public void NormalizeArray_SourceAlreadyCurrent_NoMigrationApplied()
    {
        var nodes = Parse("[{\"id\":\"n1\"}]");

        var normalized = WorkflowGraphSchema.NormalizeArray(nodes, sourceVersion: WorkflowGraphSchema.CurrentVersion);

        Assert.Equal(nodes.ToJsonString(), normalized.ToJsonString());
    }

    [Fact]
    public void NormalizeGraph_Envelope_MigratesNodesAndConnections()
    {
        WorkflowGraphSchema.RegisterMigration(1, g =>
        {
            if (g is JsonArray arr)
            {
                foreach (var item in arr)
                {
                    if (item is JsonObject obj && !obj.ContainsKey("migrated"))
                    {
                        obj["migrated"] = true;
                    }
                }
            }

            return g;
        });

        var envelope = Parse("{\"schemaVersion\":1,\"nodes\":[{\"id\":\"n1\"}],\"connections\":[{\"id\":\"c1\"}]}");

        var result = WorkflowGraphSchema.NormalizeGraph(envelope, targetVersion: 2);

        var obj = Assert.IsType<JsonObject>(result);
        Assert.Equal(2, obj["schemaVersion"]!.GetValue<int>());
        Assert.True(obj["nodes"]![0]!["migrated"]!.GetValue<bool>());
        Assert.True(obj["connections"]![0]!["migrated"]!.GetValue<bool>());
    }

    [Fact]
    public void NormalizeGraph_BareArray_TreatedAsNodesAndReadable()
    {
        var bare = Parse("[{\"id\":\"n1\"}]");

        var result = WorkflowGraphSchema.NormalizeGraph(bare);

        var obj = Assert.IsType<JsonObject>(result);
        Assert.Equal(WorkflowGraphSchema.CurrentVersion, obj["schemaVersion"]!.GetValue<int>());
        var nodes = Assert.IsType<JsonArray>(obj["nodes"]);
        Assert.Equal("n1", nodes[0]!["id"]!.GetValue<string>());
    }

    [Fact]
    public void RegisterMigration_InvalidFromVersion_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WorkflowGraphSchema.RegisterMigration(0, g => g));
    }
}

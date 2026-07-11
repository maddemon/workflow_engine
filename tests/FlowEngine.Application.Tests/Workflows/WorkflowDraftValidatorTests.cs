using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Application.Workflows;
using Xunit;

namespace FlowEngine.Application.Tests.Workflows;

/// <summary>
/// <see cref="WorkflowDraftValidator"/> 测试，覆盖结构、节点类型、端口、连接、必填参数与凭据存在性。
/// </summary>
public class WorkflowDraftValidatorTests
{
    private static readonly NodeTypeDescriptor FetchDescriptor = new()
    {
        TypeName = "fetch",
        Ports =
        [
            new PortDefinition { Name = "out", Direction = PortDirection.Output },
            new PortDefinition { Name = "in", Direction = PortDirection.Input },
        ],
        Parameters =
        [
            new ParameterDefinition { Name = "url", Required = true },
        ],
    };

    private static readonly NodeTypeDescriptor StoreDescriptor = new()
    {
        TypeName = "store",
        Ports =
        [
            new PortDefinition { Name = "in", Direction = PortDirection.Input },
        ],
        Parameters =
        [
            new ParameterDefinition { Name = "connection", Required = true, CredentialType = "connectionString" },
        ],
    };

    private static readonly NodeTypeDescriptor NoopDescriptor = new()
    {
        TypeName = "noop",
        Ports = [],
        Parameters = [],
    };

    private static WorkflowDraftValidator CreateValidator(HashSet<string>? existingCredentials = null)
    {
        var registry = new FakeNodeRegistry([FetchDescriptor, StoreDescriptor, NoopDescriptor]);
        var accessor = new FakeCredentialAccessor(existingCredentials ?? []);
        return new WorkflowDraftValidator(registry, accessor);
    }

    [Fact]
    public async Task ValidateAsync_ValidWorkflow_ReturnsValid()
    {
        var draft = JsonNode.Parse("""
        {
          "name": "valid",
          "nodes": [
            { "id": "n1", "typeName": "fetch", "isEntry": true, "parameters": { "url": "https://x" } },
            { "id": "n2", "typeName": "store", "parameters": { "connection": "mydb" } }
          ],
          "connections": [
            { "sourceNodeId": "n1", "sourcePortName": "out", "targetNodeId": "n2", "targetPortName": "in" }
          ]
        }
        """)!;

        var result = await CreateValidator(["mydb"]).ValidateAsync(draft, CancellationToken.None);

        Assert.True(result.Valid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task ValidateAsync_MissingName_ReturnsInvalid()
    {
        var draft = JsonNode.Parse("""
        {
          "name": "",
          "nodes": [ { "id": "n1", "typeName": "noop", "isEntry": true } ]
        }
        """)!;

        var result = await CreateValidator().ValidateAsync(draft, CancellationToken.None);

        Assert.False(result.Valid);
        Assert.Contains(result.Errors, e => e.Contains("工作流名称不能为空"));
    }

    [Fact]
    public async Task ValidateAsync_EmptyNodes_ReturnsInvalid()
    {
        var draft = JsonNode.Parse("""{ "name": "x", "nodes": [] }""")!;

        var result = await CreateValidator().ValidateAsync(draft, CancellationToken.None);

        Assert.False(result.Valid);
        Assert.Contains(result.Errors, e => e.Contains("nodes 必须是非空数组"));
    }

    [Fact]
    public async Task ValidateAsync_NotAnObject_ReturnsInvalid()
    {
        var result = await CreateValidator().ValidateAsync(JsonArray.Parse("[]"), CancellationToken.None);

        Assert.False(result.Valid);
        Assert.Contains(result.Errors, e => e.Contains("工作流必须是 JSON 对象"));
    }

    [Fact]
    public async Task ValidateAsync_UnknownNodeType_ReturnsInvalid()
    {
        var draft = JsonNode.Parse("""
        {
          "name": "x",
          "nodes": [ { "id": "n1", "typeName": "ghost", "isEntry": true } ]
        }
        """)!;

        var result = await CreateValidator().ValidateAsync(draft, CancellationToken.None);

        Assert.False(result.Valid);
        Assert.Contains(result.Errors, e => e.Contains("未知的节点类型") && e.Contains("ghost"));
    }

    [Fact]
    public async Task ValidateAsync_MissingRequiredParameter_ReturnsInvalid()
    {
        var draft = JsonNode.Parse("""
        {
          "name": "x",
          "nodes": [ { "id": "n1", "typeName": "fetch", "isEntry": true, "parameters": {} } ]
        }
        """)!;

        var result = await CreateValidator().ValidateAsync(draft, CancellationToken.None);

        Assert.False(result.Valid);
        Assert.Contains(result.Errors, e => e.Contains("缺少必填参数") && e.Contains("url"));
    }

    [Fact]
    public async Task ValidateAsync_NoEntryNode_ReturnsInvalid()
    {
        var draft = JsonNode.Parse("""
        {
          "name": "x",
          "nodes": [ { "id": "n1", "typeName": "noop" } ]
        }
        """)!;

        var result = await CreateValidator().ValidateAsync(draft, CancellationToken.None);

        Assert.False(result.Valid);
        Assert.Contains(result.Errors, e => e.Contains("至少需要一个入口节点"));
    }

    [Fact]
    public async Task ValidateAsync_ConnectionToMissingNode_ReturnsInvalid()
    {
        var draft = JsonNode.Parse("""
        {
          "name": "x",
          "nodes": [ { "id": "n1", "typeName": "noop", "isEntry": true } ],
          "connections": [
            { "sourceNodeId": "n1", "sourcePortName": "x", "targetNodeId": "nope", "targetPortName": "y" }
          ]
        }
        """)!;

        var result = await CreateValidator().ValidateAsync(draft, CancellationToken.None);

        Assert.False(result.Valid);
        Assert.Contains(result.Errors, e => e.Contains("引用了不存在的目标节点") && e.Contains("nope"));
    }

    [Fact]
    public async Task ValidateAsync_WrongSourcePortDirection_ReturnsInvalid()
    {
        var draft = JsonNode.Parse("""
        {
          "name": "x",
          "nodes": [
            { "id": "n1", "typeName": "fetch", "isEntry": true, "parameters": { "url": "https://x" } },
            { "id": "n2", "typeName": "store", "parameters": { "connection": "mydb" } }
          ],
          "connections": [
            { "sourceNodeId": "n1", "sourcePortName": "in", "targetNodeId": "n2", "targetPortName": "in" }
          ]
        }
        """)!;

        var result = await CreateValidator(["mydb"]).ValidateAsync(draft, CancellationToken.None);

        Assert.False(result.Valid);
        Assert.Contains(result.Errors, e => e.Contains("源端口") && e.Contains("必须是 Output"));
    }

    [Fact]
    public async Task ValidateAsync_TargetPortNotInput_ReturnsInvalid()
    {
        var draft = JsonNode.Parse("""
        {
          "name": "x",
          "nodes": [
            { "id": "n1", "typeName": "fetch", "isEntry": true, "parameters": { "url": "https://x" } },
            { "id": "n2", "typeName": "store", "parameters": { "connection": "mydb" } }
          ],
          "connections": [
            { "sourceNodeId": "n1", "sourcePortName": "out", "targetNodeId": "n2", "targetPortName": "missing" }
          ]
        }
        """)!;

        var result = await CreateValidator(["mydb"]).ValidateAsync(draft, CancellationToken.None);

        Assert.False(result.Valid);
        Assert.Contains(result.Errors, e => e.Contains("目标节点") && e.Contains("不存在 Input 端口"));
    }

    [Fact]
    public async Task ValidateAsync_MissingCredentialByParameter_ReturnsInvalid()
    {
        var draft = JsonNode.Parse("""
        {
          "name": "x",
          "nodes": [
            { "id": "n1", "typeName": "fetch", "isEntry": true, "parameters": { "url": "https://x" } },
            { "id": "n2", "typeName": "store", "parameters": { "connection": "ghostdb" } }
          ],
          "connections": [
            { "sourceNodeId": "n1", "sourcePortName": "out", "targetNodeId": "n2", "targetPortName": "in" }
          ]
        }
        """)!;

        var result = await CreateValidator(["mydb"]).ValidateAsync(draft, CancellationToken.None);

        Assert.False(result.Valid);
        Assert.Contains(result.Errors, e => e.Contains("不存在的凭据") && e.Contains("ghostdb"));
    }

    [Fact]
    public async Task ValidateAsync_MissingCredentialByExpression_ReturnsInvalid()
    {
        var draft = JsonNode.Parse("""
        {
          "name": "x",
          "nodes": [
            { "id": "n1", "typeName": "fetch", "isEntry": true,
              "parameters": { "url": "https://x?token=$credentials.missing.accessToken" } }
          ]
        }
        """)!;

        var result = await CreateValidator(["mydb"]).ValidateAsync(draft, CancellationToken.None);

        Assert.False(result.Valid);
        Assert.Contains(result.Errors, e => e.Contains("不存在的凭据") && e.Contains("missing"));
    }

    [Fact]
    public async Task ValidateAsync_ExistingCredentialByExpression_ReturnsValid()
    {
        var draft = JsonNode.Parse("""
        {
          "name": "x",
          "nodes": [
            { "id": "n1", "typeName": "fetch", "isEntry": true,
              "parameters": { "url": "https://x?token=$credentials.mydb.accessToken" } }
          ]
        }
        """)!;

        var result = await CreateValidator(["mydb"]).ValidateAsync(draft, CancellationToken.None);

        Assert.True(result.Valid);
    }

    private sealed class FakeNodeRegistry : INodeRegistry
    {
        private readonly IReadOnlyCollection<NodeTypeDescriptor> _descriptors;

        public FakeNodeRegistry(IReadOnlyCollection<NodeTypeDescriptor> descriptors)
            => _descriptors = descriptors;

        public IReadOnlyCollection<NodeTypeDescriptor> GetDescriptors() => _descriptors;

        public void Register(INodeType nodeType) => throw new System.NotSupportedException();
        public INodeType Get(string typeName) => throw new System.NotSupportedException();
        public bool TryGet(string typeName, out INodeType? nodeType)
        {
            nodeType = null;
            return false;
        }
        public IReadOnlyCollection<INodeType> GetAll() => throw new System.NotSupportedException();
        public INodeType CreateInstance(string typeName) => throw new System.NotSupportedException();
        public NodeTypeDescriptor GetDescriptor(string typeName) => throw new System.NotSupportedException();
    }

    private sealed class FakeCredentialAccessor : ICredentialAccessor
    {
        private readonly HashSet<string> _existing;

        public FakeCredentialAccessor(IEnumerable<string> existing)
            => _existing = new HashSet<string>(existing, System.StringComparer.Ordinal);

        public Task<CredentialValue> GetCredentialAsync(Guid credentialId, CancellationToken cancellationToken = default)
            => Task.FromResult(new CredentialValue
            {
                Name = "x",
                Type = "apiKey",
                Fields = new Dictionary<string, string>(),
                BinaryFields = new Dictionary<string, byte[]>(),
            });

        public Task<CredentialValue?> GetCredentialByNameAsync(string name, CancellationToken cancellationToken = default)
            => Task.FromResult(_existing.Contains(name)
                ? new CredentialValue
                {
                    Name = name,
                    Type = "connectionString",
                    Fields = new Dictionary<string, string>(),
                    BinaryFields = new Dictionary<string, byte[]>(),
                }
                : null);
    }
}

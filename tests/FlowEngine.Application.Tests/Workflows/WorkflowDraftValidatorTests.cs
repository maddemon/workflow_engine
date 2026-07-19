using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Application.Workflows;
using FlowEngine.Application.Tests.TestSupport.Fakes;
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
            new ParameterDefinition { Name = "connection", Required = true, CredentialType = "database" },
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

    [Fact]
    public void CollectMustacheErrors_MustacheInUrl_Reported()
    {
        var node = JsonNode.Parse("""{"id":"getEmployees","typeName":"httpRequest","parameters":{"url":"https://x?access_token={{$json.access_token}}"}}""")!;
        var errors = new List<string>();
        WorkflowDraftValidator.CollectMustacheErrors(node["parameters"], "getEmployees", errors);

        Assert.NotEmpty(errors);
        Assert.Contains(errors, e => e.Contains("{{") && e.Contains("url"));
    }

    [Fact]
    public void CollectMustacheErrors_ValidJs_Passes()
    {
        var node = JsonNode.Parse("""{"id":"getEmployees","typeName":"httpRequest","parameters":{"url":"'https://x?access_token=' + $json.body.access_token"}}""")!;
        var errors = new List<string>();
        WorkflowDraftValidator.CollectMustacheErrors(node["parameters"], "getEmployees", errors);

        Assert.Empty(errors);
    }

    [Fact]
    public void CollectExpressionSyntaxErrors_BareMustacheUrl_Reported()
    {
        var httpDescriptor = new NodeTypeDescriptor
        {
            TypeName = "httpRequest",
            Parameters =
            [
                new ParameterDefinition { Name = "url", Type = ParameterType.String, Hint = PresentationHint.Expression },
            ],
            Ports = [],
        };
        var node = JsonNode.Parse("""{"id":"getEmployees","typeName":"httpRequest","parameters":{"url":"https://x?access_token={{$json.access_token}}"}}""")!;
        var errors = new List<string>();
        WorkflowDraftValidator.CollectExpressionSyntaxErrors(node["parameters"], httpDescriptor, "getEmployees", errors);
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void CollectExpressionSyntaxErrors_UnbalancedParens_Reported()
    {
        var codeDescriptor = new NodeTypeDescriptor
        {
            TypeName = "script",
            Parameters =
            [
                new ParameterDefinition { Name = "code", Type = ParameterType.Code },
            ],
            Ports = [],
        };
        var node = JsonNode.Parse("""{"id":"n","typeName":"script","parameters":{"code":"return ($json.a + "}}""")!;
        var errors = new List<string>();
        WorkflowDraftValidator.CollectExpressionSyntaxErrors(node["parameters"], codeDescriptor, "n", errors);
        Assert.NotEmpty(errors);
    }

    [Fact]
    public async Task ValidateAsync_DuplicateNodeId_ReturnsInvalid()
    {
        var draft = JsonNode.Parse("""
        {
          "name": "x",
          "nodes": [
            { "id": "n1", "typeName": "noop", "isEntry": true },
            { "id": "n1", "typeName": "noop" }
          ]
        }
        """)!;

        var result = await CreateValidator().ValidateAsync(draft, CancellationToken.None);

        Assert.False(result.Valid);
        Assert.Contains(result.Errors, e => e.Contains("重复"));
    }

    [Fact]
    public async Task ValidateAsync_ConnectionsNotArray_ReturnsInvalid()
    {
        var draft = JsonNode.Parse("""
        {
          "name": "x",
          "nodes": [ { "id": "n1", "typeName": "noop", "isEntry": true } ],
          "connections": "bad"
        }
        """)!;

        var result = await CreateValidator().ValidateAsync(draft, CancellationToken.None);

        Assert.False(result.Valid);
        Assert.Contains(result.Errors, e => e.Contains("connections 必须是数组"));
    }

    [Fact]
    public async Task ValidateAsync_PortTypeIncompatible_ReturnsInvalid()
    {
        var registry = new FakeNodeRegistry([
            new NodeTypeDescriptor
            {
                TypeName = "fetch",
                Ports =
                [
                    new PortDefinition { Name = "out", Direction = PortDirection.Output, Type = PortType.AgentTool },
                ],
                Parameters = [new ParameterDefinition { Name = "url", Required = true }],
            },
            new NodeTypeDescriptor
            {
                TypeName = "store",
                Ports =
                [
                    new PortDefinition { Name = "in", Direction = PortDirection.Input, Type = PortType.Memory },
                ],
                Parameters = [],
            },
        ]);
        var validator = new WorkflowDraftValidator(registry, new FakeCredentialAccessor([]));
        var draft = JsonNode.Parse("""
        {
          "name": "x",
          "nodes": [
            { "id": "n1", "typeName": "fetch", "isEntry": true, "parameters": { "url": "https://x" } },
            { "id": "n2", "typeName": "store" }
          ],
          "connections": [
            { "sourceNodeId": "n1", "sourcePortName": "out", "targetNodeId": "n2", "targetPortName": "in" }
          ]
        }
        """)!;

        var result = await validator.ValidateAsync(draft, CancellationToken.None);

        Assert.False(result.Valid);
        Assert.Contains(result.Errors, e => e.Contains("端口类型不兼容"));
    }

    [Fact]
    public async Task ValidateAsync_IsolatedNode_ReturnsInvalid()
    {
        var draft = JsonNode.Parse("""
        {
          "name": "x",
          "nodes": [
            { "id": "n1", "typeName": "noop", "isEntry": true },
            { "id": "n2", "typeName": "noop" }
          ],
          "connections": [
            { "sourceNodeId": "n1", "sourcePortName": "x", "targetNodeId": "n1", "targetPortName": "y" }
          ]
        }
        """)!;

        var result = await CreateValidator().ValidateAsync(draft, CancellationToken.None);

        Assert.False(result.Valid);
        Assert.Contains(result.Errors, e => e.Contains("孤立节点"));
    }

    [Fact]
    public async Task ValidateAsync_ValidExpressionParameter_Passes()
    {
        var registry = new FakeNodeRegistry([
            new NodeTypeDescriptor
            {
                TypeName = "httpRequest",
                Parameters =
                [
                    new ParameterDefinition { Name = "url", Type = ParameterType.String, Hint = PresentationHint.Expression },
                ],
                Ports = [],
            },
        ]);
        var validator = new WorkflowDraftValidator(registry, new FakeCredentialAccessor([]));
        var draft = JsonNode.Parse("""
        {
          "name": "x",
          "nodes": [
            { "id": "n1", "typeName": "httpRequest", "isEntry": true, "parameters": { "url": "'https://api.com?token=' + $json.token" } }
          ]
        }
        """)!;

        var result = await validator.ValidateAsync(draft, CancellationToken.None);

        Assert.True(result.Valid);
    }

    [Fact]
    public void CollectCredentialReferences_InNestedObject_AddsNames()
    {
        var node = JsonNode.Parse("""
        {
          "parameters": {
            "headers": { "Authorization": "$credentials.apiKey.token" },
            "list": ["$credentials.db.password"]
          }
        }
        """)!;
        var names = new HashSet<string>();

        WorkflowDraftValidator.CollectCredentialReferences(node["parameters"], names);

        Assert.Contains("apiKey", names);
        Assert.Contains("db", names);
    }

    [Fact]
    public void CollectMustacheErrors_NestedObject_Reported()
    {
        var node = JsonNode.Parse("""
        {
          "id":"n",
          "typeName":"httpRequest",
          "parameters":{
            "headers": { "Authorization": "Bearer {{token}}" }
          }
        }
        """)!;
        var errors = new List<string>();

        WorkflowDraftValidator.CollectMustacheErrors(node["parameters"], "n", errors);

        Assert.Contains(errors, e => e.Contains("{{") && e.Contains("Authorization"));
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
                    Type = "database",
                    Fields = new Dictionary<string, string>(),
                    BinaryFields = new Dictionary<string, byte[]>(),
                }
                : null);
    }
}

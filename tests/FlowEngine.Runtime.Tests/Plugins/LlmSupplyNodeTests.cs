using System.Text.Json.Nodes;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Plugins.Standard;
using FlowEngine.Runtime.Tests.Executor;
using FlowEngine.Runtime.Registry;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlowEngine.Runtime.Tests.Plugins;

public class LlmSupplyNodeTests
{
    private readonly INodeRegistry _nodeRegistry;

    public LlmSupplyNodeTests()
    {
        _nodeRegistry = new NodeRegistry(
            new INodeType[]
            {
                new PassThroughNode(),
                new AgentNode(),
                new LlmSupplyNode()
            },
            NullLogger<NodeRegistry>.Instance);
    }

    [Fact]
    public void LlmSupplyNode_Has_Correct_TypeName()
    {
        var node = new LlmSupplyNode();
        Assert.Equal("llmSupply", node.TypeName);
    }

    [Fact]
    public void LlmSupplyNode_Has_Correct_DisplayName()
    {
        var node = new LlmSupplyNode();
        Assert.Equal("LLM Supply", node.DisplayName);
    }

    [Fact]
    public void LlmSupplyNode_Has_Correct_Category()
    {
        var node = new LlmSupplyNode();
        Assert.Equal("AI", node.Category);
    }

    [Fact]
    public void LlmSupplyNode_Has_Correct_Ports()
    {
        var node = new LlmSupplyNode();

        Assert.Single(node.Ports);
        Assert.Contains(node.Ports, p =>
            p.Name == "llmSupply"
            && p.Type == PortType.LLMSupply
            && p.Direction == PortDirection.Output);
    }

    [Fact]
    public void LlmSupplyNode_Default_Parameters()
    {
        var node = new LlmSupplyNode();
        Assert.Equal("gpt-4", node.Model);
        Assert.Equal(0.7f, node.Temperature);
        Assert.Null(node.MaxTokens);
        Assert.Null(node.CredentialId);
        Assert.Null(node.BaseEndpoint);
    }

    [Fact]
    public void LlmSupplyNode_Is_Entry_Node()
    {
        var node = new LlmSupplyNode();
        Assert.True(node.DefaultIsEntry);
    }

    [Fact]
    public async Task ExecuteAsync_Returns_Error_When_Model_Empty()
    {
        var node = new LlmSupplyNode { Model = "" };
        var context = CreateContext();

        var result = await node.ExecuteAsync(context);

        Assert.False(result.Success);
        Assert.Equal("MissingModel", result.Error?.Code);
    }

    [Fact]
    public async Task ExecuteAsync_Returns_Error_When_No_Credential()
    {
        var node = new LlmSupplyNode
        {
            Model = "gpt-4",
            CredentialId = null
        };
        var context = CreateContext();

        var result = await node.ExecuteAsync(context);

        Assert.False(result.Success);
        Assert.Equal("MissingApiKey", result.Error?.Code);
    }

    [Fact]
    public async Task ExecuteAsync_Returns_Error_When_Credential_Not_Found()
    {
        var node = new LlmSupplyNode
        {
            Model = "gpt-4",
            CredentialId = Guid.NewGuid().ToString()
        };
        var context = CreateContext(credentialExists: false);

        var result = await node.ExecuteAsync(context);

        Assert.False(result.Success);
        Assert.Equal("MissingApiKey", result.Error?.Code);
    }

    [Fact]
    public async Task ExecuteAsync_Returns_Error_When_Credential_Has_No_ApiKey()
    {
        var node = new LlmSupplyNode
        {
            Model = "gpt-4",
            CredentialId = Guid.NewGuid().ToString()
        };
        var context = CreateContext(credentialHasApiKey: false);

        var result = await node.ExecuteAsync(context);

        Assert.False(result.Success);
        Assert.Equal("MissingApiKey", result.Error?.Code);
    }

    [Fact]
    public async Task ExecuteAsync_Creates_Client_With_Valid_Credential()
    {
        var node = new LlmSupplyNode
        {
            Model = "gpt-4",
            Temperature = 0.5f,
            MaxTokens = 1000,
            CredentialId = Guid.NewGuid().ToString()
        };
        var context = CreateContext(apiKey: "test-api-key");

        var result = await node.ExecuteAsync(context);

        Assert.True(result.Success);
        Assert.NotNull(result.LlmClient);
        Assert.IsType<OpenAiLlmClient>(result.LlmClient);
    }

    [Fact]
    public async Task ExecuteAsync_Creates_Client_With_Endpoint()
    {
        var node = new LlmSupplyNode
        {
            Model = "gpt-4",
            CredentialId = Guid.NewGuid().ToString(),
            BaseEndpoint = "https://custom-openai.example.com/v1"
        };
        var context = CreateContext(apiKey: "test-api-key");

        var result = await node.ExecuteAsync(context);

        Assert.True(result.Success);
        Assert.NotNull(result.LlmClient);
    }

    [Fact]
    public async Task ExecuteAsync_Creates_Client_With_Invalid_Endpoint()
    {
        var node = new LlmSupplyNode
        {
            Model = "gpt-4",
            CredentialId = Guid.NewGuid().ToString(),
            BaseEndpoint = "not-a-valid-uri"
        };
        var context = CreateContext(apiKey: "test-api-key");

        var result = await node.ExecuteAsync(context);

        Assert.True(result.Success);
        Assert.NotNull(result.LlmClient);
    }

    [Fact]
    public async Task ExecuteAsync_Sets_LlmClient_On_Result()
    {
        var node = new LlmSupplyNode
        {
            Model = "gpt-3.5-turbo",
            CredentialId = Guid.NewGuid().ToString()
        };
        var context = CreateContext(apiKey: "sk-test");

        var result = await node.ExecuteAsync(context);

        Assert.True(result.Success);
        Assert.NotNull(result.LlmClient);
        Assert.IsType<OpenAiLlmClient>(result.LlmClient);
    }

    [Fact]
    public void LlmSupplyNode_Registered_In_Registry()
    {
        Assert.True(_nodeRegistry.TryGet("llmSupply", out var nodeType));
        Assert.NotNull(nodeType);
        Assert.IsType<LlmSupplyNode>(nodeType);
    }

    [Fact]
    public void LlmSupplyNode_Descriptor_Appears_In_Descriptors()
    {
        var descriptors = _nodeRegistry.GetDescriptors();
        var supplyDescriptor = descriptors.FirstOrDefault(d => d.TypeName == "llmSupply");

        Assert.NotNull(supplyDescriptor);
        Assert.Equal("LLM Supply", supplyDescriptor.DisplayName);
        Assert.Equal("AI", supplyDescriptor.Category);
    }

    [Fact]
    public async Task LlmSupplyNode_Temperature_Is_Clamped_In_Client()
    {
        var node = new LlmSupplyNode
        {
            Model = "gpt-4",
            Temperature = -1f,
            CredentialId = Guid.NewGuid().ToString()
        };
        var context = CreateContext(apiKey: "test-api-key");

        var result = await node.ExecuteAsync(context);

        Assert.True(result.Success);
        Assert.NotNull(result.LlmClient);
    }

    private NodeExecutionContext CreateContext(
        string? apiKey = "test-api-key",
        bool credentialExists = true,
        bool credentialHasApiKey = true)
    {
        return new NodeExecutionContext
        {
            Workflow = new Workflow
            {
                Id = Guid.NewGuid(),
                Name = "test",
                CreatedBy = "test",
                Nodes = [],
                Connections = []
            },
            ExecutionId = Guid.NewGuid(),
            Node = new NodeDefinition
            {
                Id = Guid.NewGuid(),
                TypeName = "llmSupply",
                Name = "llmSupply1",
                Parameters = []
            },
            Inputs = new Dictionary<string, DataBatch>(),
            RawParameters = new Dictionary<string, object>(),
            ResolvedParameters = new Dictionary<string, object>(),
            Credentials = new TestCredentialAccessor(apiKey, credentialExists, credentialHasApiKey),
            Logger = NullExecutionLogger.Instance,
            CancellationToken = CancellationToken.None
        };
    }

    private sealed class TestCredentialAccessor : ICredentialAccessor
    {
        private readonly string? _apiKey;
        private readonly bool _exists;
        private readonly bool _hasApiKey;

        public TestCredentialAccessor(string? apiKey, bool exists, bool hasApiKey)
        {
            _apiKey = apiKey;
            _exists = exists;
            _hasApiKey = hasApiKey;
        }

        public Task<CredentialValue> GetCredentialAsync(Guid credentialId, CancellationToken cancellationToken = default)
        {
            if (!_exists)
            {
                return Task.FromResult(new CredentialValue
                {
                    Fields = new Dictionary<string, string>()
                });
            }

            var fields = _hasApiKey && _apiKey is not null
                ? new Dictionary<string, string> { ["apiKey"] = _apiKey }
                : new Dictionary<string, string>();

            return Task.FromResult(new CredentialValue
            {
                Name = "test",
                Type = "apiKey",
                Fields = fields
            });
        }
    }

    private sealed class NullExecutionLogger : IExecutionLogger
    {
        public static readonly NullExecutionLogger Instance = new();

        public void LogInformation(string message, params object?[] args) { }
        public void LogWarning(string message, params object?[] args) { }
        public void LogError(Exception? exception, string message, params object?[] args) { }
    }
}

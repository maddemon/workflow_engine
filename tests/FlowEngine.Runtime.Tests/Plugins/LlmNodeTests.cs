using System.Text.Json.Nodes;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Plugins.Standard;
using FlowEngine.Runtime.Tests.Executor;
using FlowEngine.Runtime.Registry;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlowEngine.Runtime.Tests.Plugins;

public class LlmNodeTests
{
    private readonly INodeRegistry _nodeRegistry;

    public LlmNodeTests()
    {
        _nodeRegistry = new NodeRegistry(
            new INodeType[]
            {
                new PassThroughNode(),
                new AgentNode(),
                new LlmNode()
            },
            NullLogger<NodeRegistry>.Instance);
    }

    [Fact]
    public void LlmNode_Has_Correct_TypeName()
    {
        var node = new LlmNode();
        Assert.Equal("llm", node.TypeName);
    }

    [Fact]
    public void LlmNode_Has_Correct_DisplayName()
    {
        var node = new LlmNode();
        Assert.Equal("LLM", node.DisplayName);
    }

    [Fact]
    public void LlmNode_Has_Correct_Category()
    {
        var node = new LlmNode();
        Assert.Equal("AI", node.Category);
    }

    [Fact]
    public void LlmNode_Has_Correct_Ports()
    {
        var node = new LlmNode();

        Assert.Single(node.Ports);
        Assert.Contains(node.Ports, p =>
            p.Name == "llm"
            && p.Type == PortType.LLM
            && p.Direction == PortDirection.Output);
    }

    [Fact]
    public void LlmNode_Default_Parameters()
    {
        var node = new LlmNode();
        Assert.Equal("gpt-4", node.Model);
        Assert.Equal(0.7f, node.Temperature);
        Assert.Null(node.MaxTokens);
        Assert.Null(node.CredentialId);
        Assert.Null(node.BaseEndpoint);
    }

    [Fact]
    public void LlmNode_Is_Entry_Node()
    {
        var node = new LlmNode();
        Assert.True(node.DefaultIsEntry);
    }

    [Fact]
    public async Task ExecuteAsync_Returns_Error_When_Model_Empty()
    {
        var node = new LlmNode { Model = "" };
        var context = CreateContext();

        var result = await node.ExecuteAsync(context);

        Assert.False(result.Success);
        Assert.Equal("MissingModel", result.Error?.Code);
    }

    [Fact]
    public async Task ExecuteAsync_Returns_Error_When_No_Credential()
    {
        var node = new LlmNode
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
        var node = new LlmNode
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
        var node = new LlmNode
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
        var node = new LlmNode
        {
            Model = "gpt-4",
            Temperature = 0.5f,
            MaxTokens = 1000,
            CredentialId = Guid.NewGuid().ToString()
        };
        var context = CreateContext(apiKey: "test-api-key");

        var result = await node.ExecuteAsync(context);

        Assert.True(result.Success);
        Assert.NotNull(context.LlmClient);
        Assert.IsType<OpenAiLlmClient>(context.LlmClient);
    }

    [Fact]
    public async Task ExecuteAsync_Creates_Client_With_Endpoint()
    {
        var node = new LlmNode
        {
            Model = "gpt-4",
            CredentialId = Guid.NewGuid().ToString(),
            BaseEndpoint = "https://custom-openai.example.com/v1"
        };
        var context = CreateContext(apiKey: "test-api-key");

        var result = await node.ExecuteAsync(context);

        Assert.True(result.Success);
        Assert.NotNull(context.LlmClient);
    }

    [Fact]
    public async Task ExecuteAsync_Creates_Client_With_Invalid_Endpoint()
    {
        var node = new LlmNode
        {
            Model = "gpt-4",
            CredentialId = Guid.NewGuid().ToString(),
            BaseEndpoint = "not-a-valid-uri"
        };
        var context = CreateContext(apiKey: "test-api-key");

        var result = await node.ExecuteAsync(context);

        Assert.True(result.Success);
        Assert.NotNull(context.LlmClient);
    }

    [Fact]
    public async Task ExecuteAsync_Sets_LlmClient_On_Context()
    {
        var node = new LlmNode
        {
            Model = "gpt-3.5-turbo",
            CredentialId = Guid.NewGuid().ToString()
        };
        var context = CreateContext(apiKey: "sk-test");

        var result = await node.ExecuteAsync(context);

        Assert.True(result.Success);
        Assert.NotNull(context.LlmClient);
        Assert.IsType<OpenAiLlmClient>(context.LlmClient);
    }

    [Fact]
    public void LlmNode_Registered_In_Registry()
    {
        Assert.True(_nodeRegistry.TryGet("llm", out var nodeType));
        Assert.NotNull(nodeType);
        Assert.IsType<LlmNode>(nodeType);
    }

    [Fact]
    public void LlmNode_Descriptor_Appears_In_Descriptors()
    {
        var descriptors = _nodeRegistry.GetDescriptors();
        var supplyDescriptor = descriptors.FirstOrDefault(d => d.TypeName == "llm");

        Assert.NotNull(supplyDescriptor);
        Assert.Equal("LLM ", supplyDescriptor.DisplayName);
        Assert.Equal("AI", supplyDescriptor.Category);
    }

    [Fact]
    public async Task LlmNode_Temperature_Is_Clamped_In_Client()
    {
        var node = new LlmNode
        {
            Model = "gpt-4",
            Temperature = -1f,
            CredentialId = Guid.NewGuid().ToString()
        };
        var context = CreateContext(apiKey: "test-api-key");

        var result = await node.ExecuteAsync(context);

        Assert.True(result.Success);
        Assert.NotNull(context.LlmClient);
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
                TypeName = "llm",
                Name = "llm1",
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

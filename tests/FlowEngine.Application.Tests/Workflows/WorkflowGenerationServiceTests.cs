using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Configuration;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Application.Workflows;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FlowEngine.Application.Tests.Workflows;

/// <summary>
/// <see cref="WorkflowGenerationService"/> 测试，覆盖生成/校验/纠错/重试/超限/异常。
/// </summary>
public class WorkflowGenerationServiceTests
{
    private static readonly NodeTypeDescriptor FetchDescriptor = new()
    {
        TypeName = "fetch",
        Ports =
        [
            new PortDefinition { Name = "out", Direction = PortDirection.Output },
            new PortDefinition { Name = "in", Direction = PortDirection.Input },
        ],
        Parameters = [new ParameterDefinition { Name = "url", Required = true }],
    };

    private static readonly NodeTypeDescriptor StoreDescriptor = new()
    {
        TypeName = "store",
        Ports = [new PortDefinition { Name = "in", Direction = PortDirection.Input }],
        Parameters = [new ParameterDefinition { Name = "connection", Required = true, CredentialType = "connectionString" }],
    };

    private const string ValidDraft = """
    {
      "name": "gen",
      "nodes": [
        { "id": "n1", "typeName": "fetch", "isEntry": true, "parameters": { "url": "https://x" } },
        { "id": "n2", "typeName": "store", "parameters": { "connection": "mydb" } }
      ],
      "connections": [
        { "sourceNodeId": "n1", "sourcePortName": "out", "targetNodeId": "n2", "targetPortName": "in" }
      ]
    }
    """;

    private const string InvalidDraft = """
    {
      "name": "gen",
      "nodes": [ { "id": "n1", "typeName": "ghost", "isEntry": true } ]
    }
    """;

    private static WorkflowGenerationService CreateService(ILlmClient llmClient, int maxRetries = 3)
    {
        var registry = new FakeNodeRegistry([FetchDescriptor, StoreDescriptor]);
        var accessor = new FakeCredentialAccessor(["mydb"]);
        return new WorkflowGenerationService(
            () => llmClient,
            registry,
            new WorkflowDraftValidator(registry, accessor),
            new AiOptions { MaxRetries = maxRetries },
            NullLogger<WorkflowGenerationService>.Instance);
    }

    [Fact]
    public async Task GenerateAsync_ValidOnFirstAttempt_ReturnsValidDraft()
    {
        var service = CreateService(new FakeLlmClient([ValidDraft]));

        var result = await service.GenerateAsync(new WorkflowGenerationRequest("生成工作流"), CancellationToken.None);

        Assert.True(result.Valid);
        Assert.Equal(1, result.Attempts);
        Assert.NotNull(result.Draft);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task GenerateAsync_CorrectionSucceeds_ReturnsValid()
    {
        var service = CreateService(new FakeLlmClient([InvalidDraft, ValidDraft]));

        var result = await service.GenerateAsync(new WorkflowGenerationRequest("生成工作流"), CancellationToken.None);

        Assert.True(result.Valid);
        Assert.Equal(2, result.Attempts);
    }

    [Fact]
    public async Task GenerateAsync_ExhaustsRetries_ReturnsInvalid()
    {
        var service = CreateService(new FakeLlmClient(
            [InvalidDraft, InvalidDraft, InvalidDraft], fallback: InvalidDraft), maxRetries: 2);

        var result = await service.GenerateAsync(
            new WorkflowGenerationRequest("生成工作流", MaxRetries: 2), CancellationToken.None);

        Assert.False(result.Valid);
        Assert.Equal(3, result.Attempts);
        Assert.Contains(result.Errors, e => e.Contains("未知的节点类型"));
    }

    [Fact]
    public async Task GenerateAsync_EmptyDescription_ReturnsInvalid()
    {
        var service = CreateService(new FakeLlmClient([ValidDraft]));

        var result = await service.GenerateAsync(new WorkflowGenerationRequest("   "), CancellationToken.None);

        Assert.False(result.Valid);
        Assert.Equal(0, result.Attempts);
        Assert.Contains(result.Errors, e => e.Contains("描述不能为空"));
    }

    [Fact]
    public async Task GenerateAsync_NonJsonResponse_ReturnsInvalid()
    {
        var service = CreateService(new FakeLlmClient(["这不是 JSON"], fallback: "这不是 JSON"), maxRetries: 1);

        var result = await service.GenerateAsync(
            new WorkflowGenerationRequest("生成工作流", MaxRetries: 1), CancellationToken.None);

        Assert.False(result.Valid);
        Assert.Equal(2, result.Attempts);
        Assert.Contains(result.Errors, e => e.Contains("不是合法的 JSON"));
    }

    [Fact]
    public async Task GenerateAsync_LlmThrows_ReturnsInvalid()
    {
        var service = CreateService(new ThrowingLlmClient());

        var result = await service.GenerateAsync(new WorkflowGenerationRequest("生成工作流"), CancellationToken.None);

        Assert.False(result.Valid);
        Assert.Contains(result.Errors, e => e.Contains("LLM 调用失败"));
    }

    private sealed class FakeLlmClient : ILlmClient
    {
        private readonly Queue<string> _responses;
        private readonly string _fallback;

        public FakeLlmClient(IEnumerable<string> responses, string fallback = "{}")
        {
            _responses = new Queue<string>(responses);
            _fallback = fallback;
        }

        public Task<LlmResponse> ChatAsync(
            IReadOnlyList<LlmMessage> messages,
            IReadOnlyList<ToolDefinition> tools,
            CancellationToken cancellationToken = default)
        {
            var content = _responses.Count > 0 ? _responses.Dequeue() : _fallback;
            return Task.FromResult(new LlmResponse { Content = content });
        }
    }

    private sealed class ThrowingLlmClient : ILlmClient
    {
        public Task<LlmResponse> ChatAsync(
            IReadOnlyList<LlmMessage> messages,
            IReadOnlyList<ToolDefinition> tools,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("boom");
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
                ? new CredentialValue { Name = name, Type = "connectionString", Fields = new Dictionary<string, string>(), BinaryFields = new Dictionary<string, byte[]>() }
                : null);
    }
}

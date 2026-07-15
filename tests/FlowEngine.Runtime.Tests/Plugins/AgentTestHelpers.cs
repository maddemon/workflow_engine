using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Plugins.Standard;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlowEngine.Runtime.Tests.Plugins;

internal sealed class MockLlmClient : ILlmClient
{
    private readonly Func<IReadOnlyList<ToolDefinition>, CancellationToken, Task<LlmResponse>> _responder;

    public string ModelName => "test-model";

    public IReadOnlyList<LlmMessage>? LastMessages { get; private set; }

    public MockLlmClient(Func<IReadOnlyList<ToolDefinition>, LlmResponse> responder)
    {
        _responder = (tools, _) => Task.FromResult(responder(tools));
    }

    public MockLlmClient(Func<IReadOnlyList<ToolDefinition>, CancellationToken, Task<LlmResponse>> responder)
    {
        _responder = responder;
    }

    public async Task<LlmResponse> ChatAsync(
        IReadOnlyList<LlmMessage> messages,
        IReadOnlyList<ToolDefinition> tools,
        CancellationToken cancellationToken = default)
    {
        LastMessages = messages;
        return await _responder(tools, cancellationToken).ConfigureAwait(false);
    }
}

internal sealed class TestCredentialAccessor : ICredentialAccessor
{
    public Task<CredentialValue> GetCredentialAsync(Guid credentialId, CancellationToken cancellationToken = default)
        => Task.FromResult(new CredentialValue());
}

internal sealed class NullExecutionLogger : IExecutionLogger
{
    public static readonly NullExecutionLogger Instance = new();

    public void LogInformation(string message, params object?[] args) { }
    public void LogWarning(string message, params object?[] args) { }
    public void LogError(Exception? exception, string message, params object?[] args) { }
}

internal sealed class FailingTestNode : INodeType
{
    public string TypeName => "failingTest";
    public string DisplayName => "Failing Test";
    public string Category => "Test";
    public string Icon => "test";
    public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;
    public IReadOnlyList<PortDefinition> Ports { get; } =
    [
        new PortDefinition { Name = FlowConstants.PortNames.Input, Direction = PortDirection.Input, Type = PortType.Main },
        new PortDefinition { Name = FlowConstants.PortNames.Output, Direction = PortDirection.Output, Type = PortType.Main }
    ];
    public bool DefaultIsEntry => false;

    public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new NodeExecutionResult
        {
            Success = false,
            Error = new NodeError
            {
                Code = "TestFailure",
                Message = "test error",
                NodeDefinitionId = context.Node.Id
            }
        });
    }
}

internal sealed class ThrowingTestNode : INodeType
{
    public string TypeName => "throwingTest";
    public string DisplayName => "Throwing Test";
    public string Category => "Test";
    public string Icon => "test";
    public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;
    public IReadOnlyList<PortDefinition> Ports { get; } =
    [
        new PortDefinition { Name = FlowConstants.PortNames.Input, Direction = PortDirection.Input, Type = PortType.Main },
        new PortDefinition { Name = FlowConstants.PortNames.Output, Direction = PortDirection.Output, Type = PortType.Main }
    ];
    public bool DefaultIsEntry => false;

    public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException("test exception");
    }
}

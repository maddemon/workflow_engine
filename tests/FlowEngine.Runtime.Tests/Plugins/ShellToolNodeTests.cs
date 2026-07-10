using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Scripting;
using FlowEngine.Plugins.Standard;

namespace FlowEngine.Runtime.Tests.Plugins;

public class ShellToolNodeTests
{
    [Fact]
    public async Task Execute_MissingCommand_ReturnsError()
    {
        var node = new ShellToolNode { Command = "" };
        var context = CreateContext(new JsonObject { ["path"] = "test" });

        var result = await node.ExecuteAsync(context, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Equal("MissingCommand", result.Error?.Code);
    }

    [Fact]
    public async Task Execute_ResolvedValue_UsesResolvedCommand()
    {
        var node = new ShellToolNode
        {
            Command = new Script
            {
                Source = "dotnet --version",
                Language = ScriptLanguage.JavaScript,
                ReturnType = ScriptReturnType.String
            }.WithResolvedValue(JsonValue.Create("dotnet --version")),
            Shell = ShellType.Cmd,
            RunInShell = false
        };
        var context = CreateContext(new JsonObject());

        var result = await node.ExecuteAsync(context, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Equal(0, result.Output.Items[0].Data?["exitCode"]?.GetValue<int>());
        Assert.False(string.IsNullOrWhiteSpace(result.Output.Items[0].Data?["stdout"]?.GetValue<string>()));
    }

    [Fact]
    public async Task Execute_ResolvedExpression_UsesCommand()
    {
        var node = new ShellToolNode
        {
            Command = new Script
            {
                Source = "'dotnet --version'",
                Language = ScriptLanguage.JavaScript,
                ReturnType = ScriptReturnType.String
            }.WithResolvedValue(JsonValue.Create("dotnet --version")),
            Shell = ShellType.Cmd,
            RunInShell = false
        };
        var context = CreateContext(new JsonObject());

        var result = await node.ExecuteAsync(context, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Equal(0, result.Output.Items[0].Data?["exitCode"]?.GetValue<int>());
    }

    private static NodeExecutionContext CreateContext(JsonObject inputPayload)
    {
        return new NodeExecutionContext
        {
            Node = new NodeDefinition
            {
                Id = Guid.NewGuid(),
                TypeName = "shellTool",
                Name = "Test Shell",
                Parameters = [],
                Ports = [],
                ErrorStrategy = ErrorStrategy.Terminate
            },
            ExecutionId = Guid.NewGuid(),
            Inputs = new Dictionary<string, DataBatch>
            {
                [FlowConstants.PortNames.Input] = new()
                {
                    Items =
                    [
                        new DataItem
                        {
                            Data = inputPayload,
                            Success = true,
                            SourceIndex = 0
                        }
                    ]
                }
            },
            RawParameters = new Dictionary<string, object>(),
            ResolvedParameters = new Dictionary<string, object>(),
            CancellationToken = CancellationToken.None
        };
    }
}

using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Abstractions;
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

        var result = await ((INodeType)node).ExecuteAsync(context, TestContext.Current.CancellationToken);

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

        var result = await ((INodeType)node).ExecuteAsync(context, TestContext.Current.CancellationToken);

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

        var result = await ((INodeType)node).ExecuteAsync(context, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Equal(0, result.Output.Items[0].Data?["exitCode"]?.GetValue<int>());
    }

    private static NodeExecutionContext CreateContext(JsonObject inputPayload)
    {
        return new NodeExecutionContext
        {
            Node = new NodeDefinition
            {
                Id = "Test Shell",
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

    [Fact]
    public async Task Execute_RunInShellTrue_WithoutPermission_ReturnsDenied()
    {
        var node = new ShellToolNode
        {
            Command = (Script)"'echo hi'",
            Shell = ShellType.Cmd,
            RunInShell = true
        };
        var context = CreateContext(new JsonObject());
        context.AllowShellExecution = false;
        context.IsAgentInvocation = false;

        var result = await ((INodeType)node).ExecuteAsync(context, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("ShellExecutionDenied", result.Error?.Code);
    }

    [Fact]
    public async Task Execute_RunInShellTrue_AgentInvocation_ReturnsDenied_EvenWithPermission()
    {
        var node = new ShellToolNode
        {
            Command = (Script)"'echo hi'",
            Shell = ShellType.Cmd,
            RunInShell = true
        };
        var context = CreateContext(new JsonObject());
        context.AllowShellExecution = true;
        context.IsAgentInvocation = true;

        var result = await ((INodeType)node).ExecuteAsync(context, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("ShellExecutionDenied", result.Error?.Code);
    }

    [Fact]
    public async Task Execute_RunInShellTrue_WithPermission_Executes()
    {
        var node = new ShellToolNode
        {
            Command = (Script)"'echo SECURITY_OK'",
            Shell = ShellType.Cmd,
            RunInShell = true
        };
        var context = CreateContext(new JsonObject());
        context.AllowShellExecution = true;
        context.IsAgentInvocation = false;

        var result = await ((INodeType)node).ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Contains("SECURITY_OK", result.Output.Items[0].Data?["stdout"]?.GetValue<string>() ?? string.Empty);
    }

    [Fact]
    public async Task Execute_RunInShellFalse_NotGated()
    {
        // 使用跨平台真实可执行文件 `dotnet --version`：Windows 上 `echo` 是 cmd 内置命令而非独立 exe，
        // 在 RunInShell=false 时直接以进程方式启动会失败；`dotnet` 在两种平台均为真实 exe。
        var node = new ShellToolNode
        {
            Command = (Script)"'dotnet --version'",
            Shell = ShellType.Cmd,
            RunInShell = false
        };
        var context = CreateContext(new JsonObject());
        context.AllowShellExecution = false;

        var result = await ((INodeType)node).ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
    }
}

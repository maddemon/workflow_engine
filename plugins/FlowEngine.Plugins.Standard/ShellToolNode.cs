using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Attributes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Runtime.Scripting;

namespace FlowEngine.Plugins.Standard;

/// <summary>
/// Shell 类型。
/// </summary>
public enum ShellType
{
    /// <summary>Bash (Linux/macOS)</summary>
    Bash,

    /// <summary>PowerShell (Windows)</summary>
    PowerShell,

    /// <summary>CMD (Windows)</summary>
    Cmd
}

/// <summary>
/// Shell 工具节点，作为 Agent 的工具执行 shell 命令。
/// 支持 bash/powershell/cmd。
/// </summary>
public sealed class ShellToolNode : INodeType
{
    /// <inheritdoc />
    public string TypeName => "shellTool";

    /// <inheritdoc />
    public string DisplayName => "Shell Tool";

    /// <inheritdoc />
    public string Category => "AI";

    /// <inheritdoc />
    public string Icon => "terminal";

    /// <inheritdoc />
    public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;

    /// <summary>
    /// 要执行的命令，支持 JS 表达式（如 <c>'ls -la ' + input.path</c>）。
    /// </summary>
    [DisplayName("Command")]
    [Description("Command to execute. Use JS expression to build command dynamically (e.g. 'echo ' + input.message).")]
    [Hint(PresentationHint.Expression)]
    public string Command { get; set; } = string.Empty;

    /// <summary>
    /// Shell 类型。
    /// </summary>
    [Description("Shell type to use for execution.")]
    public ShellType Shell { get; set; } = ShellType.Bash;

    /// <summary>
    /// 工作目录。
    /// </summary>
    [Description("Working directory for command execution. Leave empty for current directory.")]
    public string? WorkingDirectory { get; set; }

    /// <summary>
    /// 超时时间（秒）。
    /// </summary>
    [Description("Command execution timeout in seconds.")]
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// 占位符定义列表。
    /// </summary>
    [Description("Define placeholders that LLM will fill.")]
    public List<ShellPlaceholder>? Placeholders { get; set; }

    /// <inheritdoc />
    public IReadOnlyList<PortDefinition> Ports { get; } =
    [
        new PortDefinition { Name = FlowConstants.PortNames.Input, DisplayName = "Input", Direction = PortDirection.Input, Type = PortType.Main },
        new PortDefinition { Name = FlowConstants.PortNames.Output, DisplayName = "Output", Direction = PortDirection.Output, Type = PortType.Main },
        new PortDefinition { Name = FlowConstants.PortNames.Tools, DisplayName = "Tool Output", Direction = PortDirection.Output, Type = PortType.AgentTool }
    ];

    /// <inheritdoc />
    public bool DefaultIsEntry => false;

    /// <inheritdoc />
    public async Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(Command))
            {
                return context.ErrorResult("MissingCommand", "Command is required.");
            }

            var inputData = context.InputData;
            var resolvedCommand = ScriptEngine.EvaluateAsString(Command, inputData) ?? Command;

            var result = await ExecuteCommandAsync(resolvedCommand, cancellationToken).ConfigureAwait(false);

            var outputObj = new JsonObject
            {
                ["stdout"] = result.Stdout,
                ["stderr"] = result.Stderr,
                ["exitCode"] = result.ExitCode
            };

            return context.CreateSingleResult(outputObj, result.ExitCode == 0);
        }
        catch (OperationCanceledException)
        {
            return context.ErrorResult("Cancelled", "Command execution was cancelled.");
        }
        catch (Exception ex)
        {
            return context.ErrorResult("ExecutionFailed", $"Command execution failed: {ex.Message}");
        }
    }

    private async Task<CommandResult> ExecuteCommandAsync(string command, CancellationToken cancellationToken)
    {
        var (fileName, arguments) = Shell switch
        {
            ShellType.Bash => ("bash", $"-c \"{command}\""),
            ShellType.PowerShell => ("powershell", $"-Command \"{command}\""),
            ShellType.Cmd => ("cmd", $"/c \"{command}\""),
            _ => throw new InvalidOperationException($"Unsupported shell: {Shell}")
        };

        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (!string.IsNullOrEmpty(WorkingDirectory))
        {
            psi.WorkingDirectory = WorkingDirectory;
        }

        using var process = new Process { StartInfo = psi };
        process.Start();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));

        try
        {
            var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);

            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);

            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);

            return new CommandResult
            {
                Stdout = stdout.Trim(),
                Stderr = stderr.Trim(),
                ExitCode = process.ExitCode
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Timeout
            try
            {
                process.Kill();
            }
            catch
            {
                // Ignore kill errors
            }

            return new CommandResult
            {
                Stdout = string.Empty,
                Stderr = $"Command timed out after {TimeoutSeconds} seconds.",
                ExitCode = -1
            };
        }
    }
}

/// <summary>
/// 命令执行结果。
/// </summary>
internal sealed class CommandResult
{
    /// <summary>标准输出</summary>
    public string Stdout { get; set; } = string.Empty;

    /// <summary>标准错误</summary>
    public string Stderr { get; set; } = string.Empty;

    /// <summary>退出码</summary>
    public int ExitCode { get; set; }
}

/// <summary>
/// Shell 占位符定义。
/// </summary>
public sealed class ShellPlaceholder
{
    /// <summary>
    /// 占位符名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 占位符描述。
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 是否必填。
    /// </summary>
    public bool Required { get; set; } = true;
}

using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Attributes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Exceptions;
using FlowEngine.Core.Scripting;

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
    /// 要执行的命令，支持 JS 表达式（如 <c>'ls -la ' + $json.path</c>）。
    /// 纯命令需用引号包裹为 JS 字符串（如 <c>'echo hello'</c>）。
    /// </summary>
    [DisplayName("Command")]
    [Description("Command to execute. Use JS expression to build command dynamically (e.g. 'echo ' + $json.message). Plain commands must be quoted as a JS string.")]
    [Hint(PresentationHint.Expression)]
    public Script Command { get; set; } = Script.Empty;

    /// <summary>
    /// Shell 类型。
    /// </summary>
    [Description("Shell type to use for execution.")]
    public ShellType Shell { get; set; } = ShellType.Bash;

    /// <summary>
    /// 是否通过指定 shell 解释器执行命令（高危）。
    /// 默认 <c>false</c>，命令将以「参数数组」方式直接执行（去 shell 化），
    /// 避免命令注入逃逸；置为 <c>true</c> 时回退到 shell 解释器（管道/重定向等语法生效，但存在命令注入风险）。
    /// </summary>
    [Description("Run the command through a shell interpreter (e.g. bash -c / powershell -Command). Default false (de-shelled, safe against command injection). Set true only when shell features like pipes are required.")]
    public bool RunInShell { get; set; } = false;

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
        if (Command is null || string.IsNullOrWhiteSpace(Command.Source))
        {
            return context.ErrorResult(FlowConstants.ErrorCodes.MissingCommand, "Command is required.");
        }

        return await context.CatchToResult(async ct =>
        {
            var resolvedCommand = await Command.EvaluateAsync<string>(context, cancellationToken: ct);
            if (string.IsNullOrWhiteSpace(resolvedCommand))
            {
                return context.ErrorResult(FlowConstants.ErrorCodes.MissingCommand, "Command resolution failed.");
            }

            var result = await ExecuteCommandAsync(resolvedCommand, ct).ConfigureAwait(false);

            var outputObj = new JsonObject
            {
                ["stdout"] = result.Stdout,
                ["stderr"] = result.Stderr,
                ["exitCode"] = result.ExitCode
            };

            return context.CreateSingleResult(outputObj, result.ExitCode == 0);
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<CommandResult> ExecuteCommandAsync(string command, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // 去 shell 化：默认直接以参数数组执行，避免命令注入逃逸。
        // 仅当显式 RunInShell=true 时回退到 shell 解释器（高危，需用户知晓）。
        if (RunInShell)
        {
            switch (Shell)
            {
                case ShellType.PowerShell:
                    psi.FileName = "powershell";
                    psi.ArgumentList.Add("-NoProfile");
                    psi.ArgumentList.Add("-NonInteractive");
                    psi.ArgumentList.Add("-Command");
                    psi.ArgumentList.Add(command);
                    break;
                case ShellType.Cmd:
                    psi.FileName = "cmd";
                    psi.ArgumentList.Add("/c");
                    psi.ArgumentList.Add(command);
                    break;
                default:
                    psi.FileName = "bash";
                    psi.ArgumentList.Add("-c");
                    psi.ArgumentList.Add(command);
                    break;
            }
        }
        else
        {
            var tokens = TokenizeCommand(command);
            if (tokens.Count == 0)
            {
                return new CommandResult
                {
                    Stdout = string.Empty,
                    Stderr = "Command is empty or could not be parsed.",
                    ExitCode = -1
                };
            }

            psi.FileName = tokens[0];
            for (var i = 1; i < tokens.Count; i++)
            {
                psi.ArgumentList.Add(tokens[i]);
            }
        }

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
                process.Kill(entireProcessTree: true);
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

    /// <summary>
    /// 将命令字符串拆分为程序与参数数组（支持单/双引号与反斜杠转义），用于去 shell 化直接执行。
    /// </summary>
    private static List<string> TokenizeCommand(string command)
    {
        var tokens = new List<string>();
        if (string.IsNullOrWhiteSpace(command))
        {
            return tokens;
        }

        var current = new System.Text.StringBuilder();
        var inToken = false;
        char quote = '\0';
        var hasContent = false;

        for (var i = 0; i < command.Length; i++)
        {
            var c = command[i];

            if (quote != '\0')
            {
                if (c == '\\' && quote == '"' && i + 1 < command.Length)
                {
                    current.Append(command[++i]);
                }
                else if (c == quote)
                {
                    quote = '\0';
                }
                else
                {
                    current.Append(c);
                }

                hasContent = true;
                continue;
            }

            if (c is '"' or '\'')
            {
                quote = c;
                inToken = true;
                hasContent = true;
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                if (inToken)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                    inToken = false;
                    hasContent = false;
                }

                continue;
            }

            if (c == '\\' && i + 1 < command.Length)
            {
                current.Append(command[++i]);
            }
            else
            {
                current.Append(c);
            }

            inToken = true;
            hasContent = true;
        }

        if (inToken && hasContent)
        {
            tokens.Add(current.ToString());
        }

        return tokens;
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

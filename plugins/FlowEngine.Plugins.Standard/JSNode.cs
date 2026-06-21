using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Attributes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Runtime.Scripting;
using Jint;

namespace FlowEngine.Plugins.Standard;

/// <summary>
/// 脚本语言选项。
/// </summary>
public enum ScriptLanguage
{
    /// <summary>JavaScript</summary>
    [Description("JavaScript")]
    JavaScript
}

/// <summary>
/// 代码执行节点，使用 Jint 沙箱执行 JavaScript 代码。
/// </summary>
public sealed class JSNode : INodeType
{
    private const int DefaultTimeoutMs = 5000;

    /// <inheritdoc />
    public string TypeName => "script";

    /// <inheritdoc />
    public string DisplayName => "JavaScript";

    /// <inheritdoc />
    public string Category => "Utility";

    /// <inheritdoc />
    public string Icon => "code";

    /// <inheritdoc />
    public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;

    /// <summary>
    /// 要执行的代码。
    /// </summary>
    [Description("JavaScript code to execute.")]
    [Hint(PresentationHint.CodeEditor)]
    public string Code { get; set; } = string.Empty;

    /// <inheritdoc />
    public IReadOnlyList<PortDefinition> Ports { get; } =
    [
        new PortDefinition { Name = "input", DisplayName = "Input", Direction = PortDirection.Input, Type = PortType.Main },
        new PortDefinition { Name = "output", DisplayName = "Output", Direction = PortDirection.Output, Type = PortType.Main }
    ];

    /// <inheritdoc />
    public bool DefaultIsEntry => false;

    /// <inheritdoc />
    public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(Code))
            {
                return Task.FromResult(context.ErrorResult("MissingCode", "Code 参数不能为空。"));
            }

            var inputData = context.InputData;
            using var js = JsEngine.Create(options =>
            {
                options.TimeoutInterval(TimeSpan.FromMilliseconds(DefaultTimeoutMs));
                options.CancellationToken(cancellationToken);
            });

            js.SetValue("input", inputData);
            var result = js.Run(Code);
            var outputItem = JsEngine.ToDataItem(result);

            return Task.FromResult(new NodeExecutionResult
            {
                Success = true,
                Output = new DataBatch { Items = [outputItem] }
            });
        }
        catch (OperationCanceledException)
        {
            return Task.FromResult(context.ErrorResult("Cancelled", "代码执行被取消。"));
        }
        catch (Jint.Runtime.JavaScriptException ex)
        {
            return Task.FromResult(context.ErrorResult("CodeError", $"JavaScript 执行错误: {ex.Message}"));
        }
        catch (Exception ex) when (ex.GetType().Name.Contains("Timeout"))
        {
            return Task.FromResult(context.ErrorResult("Timeout", "代码执行超时。"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(context.ErrorResult("UnexpectedError", $"代码执行发生未预期错误: {ex.Message}"));
        }
    }
}

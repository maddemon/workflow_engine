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
/// 代码执行节点，使用 Jint 沙箱执行 JavaScript 代码。
/// 支持 Run Once for All Items 和 Run Once for Each Item 两种模式。
/// </summary>
public sealed class JSNode : INodeType
{
    private const int DefaultTimeoutMs = 5000;

    /// <inheritdoc />
    public string TypeName => "script";

    /// <inheritdoc />
    public string DisplayName => "Code";

    /// <inheritdoc />
    public string Category => "Core";

    /// <inheritdoc />
    public string Icon => "code";

    /// <inheritdoc />
    public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;

    /// <summary>
    /// 执行模式。
    /// </summary>
    [Description("Run code once for all items or once for each item.")]
    public CodeExecutionMode CodeMode { get; set; } = CodeExecutionMode.RunOnceForAllItems;

    /// <summary>
    /// 要执行的代码。
    /// </summary>
    [Description("JavaScript code to execute. Access input via $input.all() or $input.first().")]
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
                return Task.FromResult(context.ErrorResult("MissingCode", "Code parameter is required."));
            }

            var inputBatch = context.Inputs.TryGetValue("input", out var batch)
                ? batch
                : new DataBatch();

            if (CodeMode == CodeExecutionMode.RunOnceForEachItem)
            {
                return ExecuteForEachItem(inputBatch, context, cancellationToken);
            }

            return ExecuteForAllItems(inputBatch, context, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return Task.FromResult(context.ErrorResult("Cancelled", "Code execution was cancelled."));
        }
        catch (Jint.Runtime.JavaScriptException ex)
        {
            return Task.FromResult(context.ErrorResult("CodeError", $"JavaScript execution error: {ex.Message}"));
        }
        catch (Exception ex) when (ex.GetType().Name.Contains("Timeout"))
        {
            return Task.FromResult(context.ErrorResult("Timeout", "Code execution timed out."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(context.ErrorResult("UnexpectedError", $"Unexpected error: {ex.Message}"));
        }
    }

    private Task<NodeExecutionResult> ExecuteForAllItems(
        DataBatch inputBatch,
        NodeExecutionContext context,
        CancellationToken cancellationToken)
    {
        using var js = JsEngine.Create();

        // Provide $input helper as a simple object
        var inputItems = inputBatch.Items.Select(i => (object?)i.Data).ToList();
        var inputHelper = new InputHelper(inputItems);
        js.SetValue("$input", inputHelper);

        var result = js.Run(Code);
        var outputItem = JsEngine.ToDataItem(result);

        return Task.FromResult(new NodeExecutionResult
        {
            Success = true,
            Output = new DataBatch { Items = [outputItem] }
        });
    }

    private Task<NodeExecutionResult> ExecuteForEachItem(
        DataBatch inputBatch,
        NodeExecutionContext context,
        CancellationToken cancellationToken)
    {
        var outputItems = new List<DataItem>();

        foreach (var item in inputBatch.Items)
        {
            using var js = JsEngine.Create();

            // Provide $input helper for current item
            var allItems = inputBatch.Items.Select(i => (object?)i.Data).ToList();
            var inputHelper = new InputHelper(allItems, item.Data);
            js.SetValue("$input", inputHelper);

            var result = js.Run(Code);
            var outputItem = JsEngine.ToDataItem(result);
            outputItems.Add(outputItem);
        }

        return Task.FromResult(new NodeExecutionResult
        {
            Success = true,
            Output = new DataBatch { Items = outputItems }
        });
    }
}

/// <summary>
/// Helper class for $input in JS code.
/// </summary>
public sealed class InputHelper
{
    private readonly List<object?> _allItems;
    private readonly object? _currentItem;

    /// <summary>
    /// All input items.
    /// </summary>
    public List<object?> All => _allItems;

    /// <summary>
    /// First input item.
    /// </summary>
    public object? First => _allItems.FirstOrDefault();

    /// <summary>
    /// Current item (in RunOnceForEachItem mode).
    /// </summary>
    public object? Item => _currentItem;

    /// <summary>
    /// Item count.
    /// </summary>
    public int Count => _allItems.Count;

    /// <summary>
    /// Create InputHelper with all items.
    /// </summary>
    public InputHelper(List<object?> allItems)
    {
        _allItems = allItems;
        _currentItem = null;
    }

    /// <summary>
    /// Create InputHelper with all items and current item.
    /// </summary>
    public InputHelper(List<object?> allItems, object? currentItem)
    {
        _allItems = allItems;
        _currentItem = currentItem;
    }
}

/// <summary>
/// 代码执行模式。
/// </summary>
public enum CodeExecutionMode
{
    /// <summary>所有项目执行一次</summary>
    RunOnceForAllItems,

    /// <summary>每个项目执行一次</summary>
    RunOnceForEachItem
}

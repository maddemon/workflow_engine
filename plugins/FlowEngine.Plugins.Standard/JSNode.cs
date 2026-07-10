using FlowEngine.Core;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Attributes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Exceptions;
using FlowEngine.Core.Scripting;

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
    public Script Code { get; set; } = Script.Empty;

    /// <inheritdoc />
    public IReadOnlyList<PortDefinition> Ports { get; } =
    [
        new PortDefinition { Name = FlowConstants.PortNames.Input, DisplayName = "Input", Direction = PortDirection.Input, Type = PortType.Main },
        new PortDefinition { Name = FlowConstants.PortNames.Output, DisplayName = "Output", Direction = PortDirection.Output, Type = PortType.Main }
    ];

    /// <inheritdoc />
    public bool DefaultIsEntry => false;

    /// <inheritdoc />
    public async Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            if (Code is null || string.IsNullOrWhiteSpace(Code.Source))
            {
                return context.ErrorResult("MissingCode", "Code parameter is required.");
            }

            var inputBatch = context.Inputs.TryGetValue(FlowConstants.PortNames.Input, out var batch)
                ? batch
                : new DataBatch();

            if (CodeMode == CodeExecutionMode.RunOnceForEachItem)
            {
                return await ExecuteForEachItem(inputBatch, context, cancellationToken).ConfigureAwait(false);
            }

            return await ExecuteForAllItems(inputBatch, context, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return context.ErrorResult("Cancelled", "Code execution was cancelled.");
        }
        catch (ScriptErrorException ex)
        {
            return context.ErrorResult("CodeError", $"JavaScript execution error: {ex.Message}");
        }
        catch (Exception ex) when (ex.GetType().Name.Contains("Timeout"))
        {
            return context.ErrorResult("Timeout", "Code execution timed out.");
        }
        catch (Exception ex)
        {
            return context.ErrorResult("UnexpectedError", $"Unexpected error: {ex.Message}");
        }
    }

    private async Task<NodeExecutionResult> ExecuteForAllItems(
        DataBatch inputBatch,
        NodeExecutionContext context,
        CancellationToken cancellationToken)
    {
        var allItems = inputBatch.Items.Select(i => (object?)i.Data).ToList();
        var currentItem = allItems.Count > 0 ? allItems[0] as JsonNode : null;

        var inputContext = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["executionId"] = context.ExecutionId,
            ["runIndex"] = context.RunIndex,
            ["nodeName"] = context.Node.Name,
            ["nodeType"] = context.Node.TypeName,
            ["workflowId"] = context.Workflow.Id,
        };

        // item 自动注入 $json；仅需额外暴露 $input 容器。
        var result = await Code.EvaluateAsync<JsonNode>(context, currentItem,
            globals: new (string, object?)[]
            {
                ("$input", new InputContainer(allItems, currentItem, context.RawParameters, inputContext)),
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var outputItem = ToDataItem(result);

        return new NodeExecutionResult
        {
            Success = true,
            Output = new DataBatch { Items = [outputItem] }
        };
    }

    private async Task<NodeExecutionResult> ExecuteForEachItem(
        DataBatch inputBatch,
        NodeExecutionContext context,
        CancellationToken cancellationToken)
    {
        var outputItems = new List<DataItem>();

        for (var itemIndex = 0; itemIndex < inputBatch.Items.Count; itemIndex++)
        {
            var item = inputBatch.Items[itemIndex];
            var result = await Code.EvaluateAsync<JsonNode>(context, item.Data, itemIndex, cancellationToken: cancellationToken).ConfigureAwait(false);
            outputItems.Add(ToDataItem(result));
        }

        return new NodeExecutionResult
        {
            Success = true,
            Output = new DataBatch { Items = outputItems }
        };
    }

    private static DataItem ToDataItem(JsonNode? json)
    {
        return new DataItem
        {
            Data = json,
            Success = true,
            SourceIndex = 0
        };
    }
}

/// <summary>
/// Helper class for $input in JS code.
/// </summary>
[Obsolete("JSNode 已迁移到 InputContainer，请使用 $input.all()/first()/item()/count()")]
public sealed class InputHelper
{
    private readonly List<object?> _allItems;
    private readonly object? _currentItem;

    /// <summary>
    /// All input items.
    /// </summary>
    public List<object?> All() => _allItems;

    /// <summary>
    /// First input item.
    /// </summary>
    public object? First() => _allItems.FirstOrDefault();

    /// <summary>
    /// Current item (in RunOnceForEachItem mode).
    /// </summary>
    public object? Item() => _currentItem;

    /// <summary>
    /// Item count.
    /// </summary>
    public int Count() => _allItems.Count;

    /// <summary>
    /// Create InputHelper with all items.
    /// </summary>
    public InputHelper(List<object?> allItems)
    {
        _allItems = allItems.Select(ConvertToClr).ToList();
        _currentItem = null;
    }

    /// <summary>
    /// Create InputHelper with all items and current item.
    /// </summary>
    public InputHelper(List<object?> allItems, object? currentItem)
    {
        _allItems = allItems.Select(ConvertToClr).ToList();
        _currentItem = ConvertToClr(currentItem);
    }

    private static object? ConvertToClr(object? value)
    {
        return value switch
        {
            JsonObject obj => obj.ToDictionary(p => p.Key, p => ConvertToClr(p.Value)),
            JsonArray arr => arr.Select(ConvertToClr).ToList(),
            JsonValue val => val.GetValue<object?>(),
            JsonNode node => node.ToJsonString(),
            _ => value
        };
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

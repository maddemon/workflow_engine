using System.Text.Json.Nodes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Exceptions;
using FlowEngine.Core.Scripting.Models;

namespace FlowEngine.Core.Scripting;

/// <summary>
/// 绑定到单一 <see cref="JsEngine"/> 实例的预编译脚本会话。
/// 适用于逐 item 复用引擎，避免频繁创建沙箱。
/// </summary>
public sealed class PreparedScriptSession : IDisposable
{
    private readonly JsEngine _engine;
    private bool _disposed;

    /// <summary>
    /// 初始化 <see cref="PreparedScriptSession"/>。
    /// </summary>
    public PreparedScriptSession(JsEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    }

    /// <summary>
    /// 在当前会话引擎上执行指定脚本。
    /// </summary>
    public Task<ScriptResult> RunAsync(PreparedScript prepared, ScriptContext context, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(prepared);
        ArgumentNullException.ThrowIfNull(context);

        return Task.Run(() =>
        {
            try
            {
                PreparedScript.InjectScope(_engine, context);
                var raw = _engine.EvaluatePrepared(prepared.Inner);
                return new ScriptResult(prepared.Original, raw);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var error = new ScriptErrorException(prepared.Original, ex.Message, ex);
                return new ScriptResult(prepared.Original, error);
            }
        }, cancellationToken);
    }

    /// <summary>
    /// 在当前会话引擎上为指定 item 执行脚本，并注入逐项作用域。
    /// </summary>
    public Task<ScriptResult> RunForItemAsync(
        PreparedScript prepared,
        ScriptContext context,
        JsonNode? currentItem,
        int itemIndex,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(prepared);
        ArgumentNullException.ThrowIfNull(context);

        return Task.Run(() =>
        {
            try
            {
                _engine.ApplyGlobalVariables(context.NodeContext);

                var allItems = GetAllItems(context.NodeContext, currentItem);
                _engine.ApplyItemScope(context.NodeContext, currentItem, allItems, itemIndex);

                foreach (var (key, value) in context.ExtraGlobals)
                {
                    if (!string.IsNullOrEmpty(key))
                    {
                        _engine.SetValue(key, value);
                    }
                }

                var raw = _engine.EvaluatePrepared(prepared.Inner);
                return new ScriptResult(prepared.Original, raw);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var error = new ScriptErrorException(prepared.Original, ex.Message, ex);
                return new ScriptResult(prepared.Original, error);
            }
        }, cancellationToken);
    }

    private static List<object?> GetAllItems(NodeExecutionContext context, JsonNode? currentItem)
    {
        var allItems = new List<object?>();

        if (context.Inputs.TryGetValue(FlowConstants.PortNames.Input, out var batch))
        {
            foreach (var item in batch.Items)
            {
                allItems.Add(item.Data);
            }
        }

        if (allItems.Count == 0 && currentItem is not null)
        {
            allItems.Add(currentItem);
        }

        return allItems;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _engine.Dispose();
        }
    }
}

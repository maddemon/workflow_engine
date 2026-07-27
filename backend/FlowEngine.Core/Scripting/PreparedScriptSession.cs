using System.Text.Json.Nodes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Exceptions;

namespace FlowEngine.Core.Scripting;

/// <summary>
/// 绑定到单一 <see cref="JsEngine"/> 实例的预编译脚本会话。
/// 适用于逐 item 复用引擎，避免频繁创建沙箱。
/// </summary>
public sealed class PreparedScriptSession : IDisposable
{
    private readonly JsEngine _engine;
    private readonly bool _ownsEngine;
    private bool _disposed;

    /// <summary>
    /// 初始化 <see cref="PreparedScriptSession"/>。
    /// </summary>
    /// <param name="engine">要绑定的 JS 引擎。</param>
    /// <param name="ownsEngine">为 <c>true</c> 时 disposing 会话将同时释放引擎；
    /// 为 <c>false</c> 时引擎由调用方负责释放（用于逐 item 复用同一引擎的场景）。</param>
    public PreparedScriptSession(JsEngine engine, bool ownsEngine = false)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _ownsEngine = ownsEngine;
    }

    /// <summary>
    /// 在当前会话引擎上执行指定脚本。
    /// </summary>
    public Task<ScriptResult> RunAsync(PreparedScript prepared, ScriptContext context, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(prepared);
        ArgumentNullException.ThrowIfNull(context);

        if (prepared.CompileError is not null)
        {
            return Task.FromResult(new ScriptResult(prepared.Original, prepared.CompileError));
        }

        return Task.Run(async () =>
        {
            try
            {
                // 实例级串行：把"作用域注入 + 求值"作为原子关键区，
                // 避免同一引擎并发执行多个 item 时作用域互相覆盖导致结果错乱。
                using (await _engine.LockAsync(cancellationToken).ConfigureAwait(false))
                {
                    PreparedScript.InjectScope(_engine, context);

                    // 快照注入后的全局自有键（含辅助函数与本次作用域），求值后仅删除脚本新增的键，
                    // 避免跨次调用（同一会话复用引擎）作用域泄漏。
                    var snapshot = _engine.GetGlobalOwnKeys();
                    var raw = _engine.EvaluatePreparedCore(prepared.Inner);
                    RestoreGlobalSnapshot(_engine, snapshot);
                    return new ScriptResult(prepared.Original, raw);
                }
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

        if (prepared.CompileError is not null)
        {
            return Task.FromResult(new ScriptResult(prepared.Original, prepared.CompileError));
        }

        return Task.Run(async () =>
        {
            try
            {
                // 实例级串行：把"作用域注入 + 求值"作为原子关键区，
                // 避免同一引擎并发执行多个 item 时作用域互相覆盖导致结果错乱。
                using (await _engine.LockAsync(cancellationToken).ConfigureAwait(false))
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

                    // 快照注入后的全局自有键（含辅助函数与本 item 逐项作用域），
                    // 求值后仅删除脚本在本 item 新增的键（如 globalThis.x = 1），
                    // 避免污染后续 item；逐项注入的作用域变量会被重新设置，故予保留。
                    var snapshot = _engine.GetGlobalOwnKeys();
                    var raw = _engine.EvaluatePreparedCore(prepared.Inner);
                    RestoreGlobalSnapshot(_engine, snapshot);
                    return new ScriptResult(prepared.Original, raw);
                }
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

    /// <summary>
    /// 求值结束后，删除快照之后新增的全局自有键，恢复至注入作用域后的状态。
    /// 保留快照内的键（引擎辅助函数 console/now/jmespath/length/trim、逐项作用域变量 $json/$input 等、
    /// 以及 <see cref="ExecutionScope.ApplyGlobalVariables"/> / <see cref="ExecutionScope.ApplyItemScope"/> 注入的全局变量），
    /// 仅清除脚本在求值期间自行写入的全局（如 <c>globalThis.x = 1</c>），避免跨 item 泄漏。
    /// </summary>
    private static void RestoreGlobalSnapshot(JsEngine engine, IReadOnlyCollection<string> snapshot)
    {
        foreach (var key in engine.GetGlobalOwnKeys())
        {
            if (!snapshot.Contains(key))
            {
                engine.DeleteGlobal(key);
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            if (_ownsEngine)
            {
                _engine.Dispose();
            }
        }
    }
}

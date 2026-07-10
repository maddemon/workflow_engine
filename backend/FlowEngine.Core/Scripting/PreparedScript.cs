using FlowEngine.Core.Exceptions;
using FlowEngine.Core.Scripting.Models;
using Jint.Native;

namespace FlowEngine.Core.Scripting;

using JintPreparedScript = Jint.Prepared<Acornima.Ast.Script>;

/// <summary>
/// 已编译并可复用的脚本。包含原始脚本、缓存键与 Jint 预编译产物。
/// </summary>
public sealed class PreparedScript
{
    /// <summary>
    /// 原始脚本。
    /// </summary>
    public Script Original { get; }

    /// <summary>
    /// 缓存键（SHA256(Source)）。
    /// </summary>
    public string CacheKey { get; }

    internal JintPreparedScript Inner { get; }

    /// <summary>
    /// 初始化 <see cref="PreparedScript"/>。
    /// </summary>
    internal PreparedScript(Script original, string cacheKey, JintPreparedScript inner, ScriptErrorException? compileError = null)
    {
        Original = original;
        CacheKey = cacheKey;
        Inner = inner;
        CompileError = compileError;
    }

    /// <summary>
    /// 编译阶段捕获到的错误（如有）。存在该错误时，执行将直接返回失败结果。
    /// </summary>
    internal ScriptErrorException? CompileError { get; }

    /// <summary>
    /// 使用新建的 <see cref="JsEngine"/> 异步执行脚本。
    /// </summary>
    public async Task<ScriptResult> RunAsync(ScriptContext context, CancellationToken cancellationToken = default)
    {
        using var engine = JsEngine.Create();
        return await RunAsync(context, engine, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 使用指定的 <see cref="JsEngine"/> 异步执行脚本。
    /// </summary>
    public Task<ScriptResult> RunAsync(ScriptContext context, JsEngine engine, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(engine);

        if (CompileError is not null)
        {
            return Task.FromResult(new ScriptResult(Original, CompileError));
        }

        return Task.Run(() =>
        {
            try
            {
                InjectScope(engine, context);
                var raw = engine.EvaluatePrepared(Inner);
                return new ScriptResult(Original, raw);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var error = new ScriptErrorException(Original, ex.Message, ex);
                return new ScriptResult(Original, error);
            }
        }, cancellationToken);
    }

    /// <summary>
    /// 创建绑定到指定 <see cref="JsEngine"/> 的可复用会话。
    /// </summary>
    public PreparedScriptSession CreateSession(JsEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        return new PreparedScriptSession(engine);
    }

    internal static void InjectScope(JsEngine engine, ScriptContext context)
    {
        engine.ApplyGlobalVariables(context.NodeContext);

        foreach (var (key, value) in context.ExtraGlobals)
        {
            if (!string.IsNullOrEmpty(key))
            {
                engine.SetValue(key, value);
            }
        }
    }
}

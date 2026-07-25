using System.Text.Json;
using System.Text.Json.Nodes;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Exceptions;
using FlowEngine.Core.Http;
using FlowEngine.Core.Scripting;
using Microsoft.Extensions.Logging;

namespace FlowEngine.Core.Entities;

/// <summary>
/// A-4：将 <see cref="NodeExecutionContext"/> 中的纯工具方法提取为扩展方法，使上下文回归“数据载体”职责，
/// 避免上帝对象。调用方仍可沿用 <c>context.Ok(...)</c> 等实例语法，序列化形状（公共属性）保持不变。
/// </summary>
public static class NodeExecutionContextExtensions
{
    /// <summary>
    /// 获取参数值，优先从 ResolvedParameters 获取，其次从 RawParameters 获取。
    /// </summary>
    public static T? GetParameter<T>(this NodeExecutionContext context, string name) where T : class
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.ResolvedParameters.TryGetValue(name, out var value) && value is T typed)
        {
            return typed;
        }

        if (context.RawParameters.TryGetValue(name, out var rawValue) && rawValue is T rawTyped)
        {
            return rawTyped;
        }

        return null;
    }

    /// <summary>
    /// 创建错误结果。
    /// </summary>
    public static NodeExecutionResult ErrorResult(this NodeExecutionContext context, string code, string message)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new NodeExecutionResult
        {
            Success = false,
            Error = new NodeError
            {
                Code = code,
                Message = message,
                NodeDefinitionId = context.Node.Id
            }
        };
    }

    /// <summary>
    /// 从输入端口获取 JsonNode 数据。
    /// </summary>
    public static JsonNode? GetInputPayload(this NodeExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Inputs.TryGetValue(FlowConstants.PortNames.Input, out var batch) && batch.Items.Count > 0)
        {
            return batch.Items[0].Data;
        }

        return null;
    }

    /// <summary>
    /// 解析凭据并返回完整的 <see cref="CredentialValue"/>。
    /// 支持按凭据 ID（Guid）或凭据名称解析。
    /// 节点通过此方法获取完整凭据值对象，不直接接触 <see cref="ICredentialAccessor"/>。
    /// </summary>
    public static async Task<CredentialValue?> ResolveCredentialAsync(
        this NodeExecutionContext context, string? idOrName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (string.IsNullOrEmpty(idOrName))
        {
            return null;
        }

        try
        {
            if (Guid.TryParse(idOrName, out var id))
            {
                return await context.Credentials.GetCredentialAsync(id, cancellationToken).ConfigureAwait(false);
            }

            return await context.Credentials.GetCredentialByNameAsync(idOrName, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            context.Logger?.LogError(ex, "Failed to resolve credential {CredentialId}.", idOrName);
            return null;
        }
    }

    /// <summary>
    /// SSRF 预检。返回 null 表示安全，返回 ErrorResult 表示请求被拦截。
    /// </summary>
    public static NodeExecutionResult? GuardSsrf(this NodeExecutionContext context, string? url, string code = FlowConstants.ErrorCodes.SsrfBlocked)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        if (SsrfGuard.IsInternalTarget(url))
        {
            return context.ErrorResult(code, "Target URL points to a blocked internal/loopback address.");
        }

        return null;
    }

    /// <summary>
    /// 创建单个数据项的结果。
    /// </summary>
    public static NodeExecutionResult CreateSingleResult(this NodeExecutionContext context, JsonNode? data, bool success = true)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new NodeExecutionResult
        {
            Success = success,
            Output = new DataBatch
            {
                Items =
                [
                    new DataItem
                    {
                        Data = data,
                        Success = success,
                        SourceIndex = 0
                    }
                ]
            }
        };
    }

    /// <summary>
    /// 获取指定端口的输入批次。端口不存在时返回空批次。
    /// </summary>
    /// <param name="portName">端口名称，默认 Input。</param>
    public static DataBatch GetInputBatch(this NodeExecutionContext context, string portName = FlowConstants.PortNames.Input)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.Inputs.TryGetValue(portName, out var batch) ? batch : new DataBatch();
    }

    /// <summary>
    /// 创建单条数据项的成功结果。
    /// </summary>
    public static NodeExecutionResult Ok(this NodeExecutionContext context, JsonNode? data)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new NodeExecutionResult
        {
            Success = true,
            Output = new DataBatch
            {
                Items =
                [
                    new DataItem
                    {
                        Data = data,
                        Success = true,
                        SourceIndex = 0
                    }
                ]
            }
        };
    }

    /// <summary>
    /// 使用已有批次创建成功结果。
    /// </summary>
    public static NodeExecutionResult Ok(this NodeExecutionContext context, DataBatch batch)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new NodeExecutionResult
        {
            Success = true,
            Output = batch
        };
    }

    /// <summary>
    /// 统一捕获异常并转换为 ErrorResult。适用于无资源清理的简单节点。
    /// 对于有事务/资源的节点（如 DbUpsertNode），请使用 <see cref="ToErrorResult"/>。
    /// </summary>
    public static async Task<NodeExecutionResult> CatchToResult(
        this NodeExecutionContext context,
        Func<CancellationToken, Task<NodeExecutionResult>> exec,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            return await exec(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return context.ErrorResult(FlowConstants.ErrorCodes.Cancelled, "Operation was cancelled.");
        }
        catch (ScriptErrorException ex)
        {
            return new NodeExecutionResult
            {
                Success = false,
                Error = NodeErrorFactory.Sanitize(ex, FlowConstants.ErrorCodes.ScriptError, context.Node.Id.ToString(), "脚本执行出错。")
            };
        }
        catch (TimeoutException)
        {
            return context.ErrorResult(FlowConstants.ErrorCodes.Timeout, "Operation timed out.");
        }
        catch (Exception ex)
        {
            return new NodeExecutionResult
            {
                Success = false,
                Error = NodeErrorFactory.Sanitize(ex, FlowConstants.ErrorCodes.UnexpectedError, context.Node.Id.ToString())
            };
        }
    }

    /// <summary>
    /// 将异常轻量映射为 NodeError，供有事务/资源的节点在自己的 catch 块内使用。
    /// </summary>
    public static NodeError ToErrorResult(this NodeExecutionContext context, Exception ex)
    {
        ArgumentNullException.ThrowIfNull(context);

        return ex switch
        {
            OperationCanceledException => new NodeError
            {
                Code = FlowConstants.ErrorCodes.Cancelled,
                Message = "Operation was cancelled.",
                NodeDefinitionId = context.Node.Id
            },
            ScriptErrorException scriptEx => NodeErrorFactory.Sanitize(scriptEx, FlowConstants.ErrorCodes.ScriptError, context.Node.Id.ToString(), "脚本执行出错。"),
            _ => NodeErrorFactory.Sanitize(ex, FlowConstants.ErrorCodes.UnexpectedError, context.Node.Id.ToString())
        };
    }

    /// <summary>
    /// 尝试解析 JSON 字符串为 JsonDocument。调用方负责 Dispose。
    /// </summary>
    public static bool TryParseJson(this NodeExecutionContext context, string raw, out JsonDocument doc, out string? errorCode)
    {
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            doc = JsonDocument.Parse(raw);
            errorCode = null;
            return true;
        }
        catch (JsonException)
        {
            doc = null!;
            errorCode = "InvalidJson";
            return false;
        }
    }

    /// <summary>
    /// 尝试解析 JSON 字符串为强类型对象。
    /// </summary>
    public static bool TryParseJson<T>(this NodeExecutionContext context, string raw, out T? result, out string? errorCode, JsonSerializerOptions? opts = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            result = JsonSerializer.Deserialize<T>(raw, opts);
            if (result is null)
            {
                errorCode = "InvalidJson";
                return false;
            }

            errorCode = null;
            return true;
        }
        catch (JsonException)
        {
            result = default;
            errorCode = "InvalidJson";
            return false;
        }
    }
}

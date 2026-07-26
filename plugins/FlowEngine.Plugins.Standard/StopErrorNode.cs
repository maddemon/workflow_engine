using System.ComponentModel;
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
/// 停止并报错节点：主动中止当前执行分支，向上返回携带错误码与消息的失败结果。
/// <para>该节点仅含输入端口、无输出端口——执行即中止，下游节点不会被执行。</para>
/// </summary>
[NodeMeta(TypeName = "stopError", DisplayName = "Stop and Error", Category = NodeCategory.Flow, Icon = "alert", DefaultIsEntry = false)]
[Port(FlowConstants.PortNames.Input, "Input", PortDirection.Input, PortType.Main)]
public sealed class StopErrorNode : NodeBase
{
    /// <summary>
    /// 停止时返回的错误消息，可为字面量或 JS 表达式（支持 <c>$json</c> / <c>$input</c> 等注入）。
    /// </summary>
    [Hint(PresentationHint.Script)]
    [Description("Error message surfaced when stopping. May be a literal or a JS expression (e.g. $json.message).")]
    public Script ErrorMessage { get; set; } = new();

    /// <summary>
    /// 错误码，用于下游错误分类；默认 <c>StopAndError</c>。
    /// </summary>
    [Description("Error code returned to the execution for downstream classification. Defaults to 'StopAndError'.")]
    public string ErrorCode { get; set; } = "StopAndError";

    /// <inheritdoc />
    /// <remarks>
    /// 通过抛出 <see cref="NodeExecutionException"/> 主动中止当前分支（§backend-code-rules §10：
    /// 不向日志输出消息内容，避免泄露凭据等敏感信息）。<see cref="ErrorMessage"/> 表达式求值失败时同样返回错误结果而非抛出。
    /// </remarks>
    public override async Task<NodeHandlerOutput> ExecuteAsync(NodeInput input, CancellationToken ct)
    {
        var message = await ResolveMessageAsync(input, ct).ConfigureAwait(false);
        var code = string.IsNullOrWhiteSpace(ErrorCode) ? "StopAndError" : ErrorCode;

        throw new NodeExecutionException(code, message);
    }

    private async Task<string> ResolveMessageAsync(NodeInput input, CancellationToken cancellationToken)
    {
        var source = ErrorMessage.Source;
        if (string.IsNullOrWhiteSpace(source))
        {
            return string.Empty;
        }

        // 合法 JSON 字面量（字符串/数字/布尔）按字面量处理；否则作为 JS 表达式求值；
        // 求值失败则回退为字面量字符串（容错，与 SetNode 行为一致）。
        // 空源码已在上面处理；此处 source 非空。
        if (TryParseJsonLiteral(source!, out var literal) && literal is not null)
        {
            return LiteralToString(literal);
        }

        try
        {
            var item = input.InputBatch.Items.Count > 0 ? input.InputBatch.Items[0].Data : null;
            return await EvaluateItemAsync<string>(ErrorMessage, item, 0, cancellationToken).ConfigureAwait(false) ?? source;
        }
        catch (ScriptErrorException)
        {
            // 表达式求值失败：回退为字面量，不向上抛出（遵循"主动中止而非抛异常"的契约）。
            return source!;
        }
    }

    private static bool TryParseJsonLiteral(string source, out JsonNode? literal)
    {
        literal = null;
        try
        {
            var node = JsonNode.Parse(source);
            if (node is JsonValue)
            {
                literal = node;
                return true;
            }
        }
        catch
        {
            // 非合法 JSON 字面量，按表达式处理
        }

        return false;
    }

    private static string LiteralToString(JsonNode literal)
    {
        if (literal is JsonValue value && value.TryGetValue<string>(out var str))
        {
            return str;
        }

        return literal.ToJsonString();
    }
}

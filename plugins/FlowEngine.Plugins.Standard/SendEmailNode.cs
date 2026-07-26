using System.ComponentModel;
using System.Net;
using System.Net.Mail;
using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Attributes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Exceptions;
using FlowEngine.Core.Scripting;
using FlowEngine.Core.Tools;

namespace FlowEngine.Plugins.Standard;

/// <summary>
/// 发送邮件节点。基于 BCL <see cref="SmtpClient"/> / <see cref="MailMessage"/> 通过 SMTP 发送邮件，
/// 凭据使用内置 <c>smtp</c> 类型。支持可选的 <see cref="PickupDirectory"/>（SpecifiedPickupDirectory），
/// 将 <c>.eml</c> 写入指定目录而非联网发送，便于离线暂存投递与单元测试。
/// </summary>
[NodeMeta(TypeName = "sendEmail", DisplayName = "Send Email", Category = NodeCategory.Network, Icon = "email", DefaultIsEntry = false)]
[Port(FlowConstants.PortNames.Input, "Input", PortDirection.Input, PortType.Main)]
[Port(FlowConstants.PortNames.Output, "Output", PortDirection.Output, PortType.Main)]
public sealed class SendEmailNode : NodeBase
{
    [Inject] public NodeExecutionContext Ctx { get; private set; } = null!;
    [Inject] public IExecutionLogger? Logger { get; private set; }
    /// <summary>
    /// SMTP 凭据（类型为 <c>smtp</c>）。字段：host/port/user/password/useSsl。密码为 secret，绝不输出到日志或异常。
    /// </summary>
    [Credential("smtp")]
    [Description("SMTP credential (type: smtp). Fields: host/port/user/password/useSsl.")]
    public CredentialValue? Connection { get; set; }

    /// <summary>
    /// 收件人地址。JS 表达式；多个地址以逗号 / 分号 / 换行分隔。必填。
    /// </summary>
    [Hint(PresentationHint.Script)]
    [Description("Recipient address(es). JS expression; multiple addresses separated by comma/semicolon/newline.")]
    public Script? To { get; set; }

    /// <summary>
    /// 邮件主题。JS 表达式。必填。
    /// </summary>
    [Hint(PresentationHint.Script)]
    [Description("Email subject. JS expression.")]
    public Script? Subject { get; set; }

    /// <summary>
    /// 邮件正文（纯文本或 HTML）。JS 表达式。必填。
    /// </summary>
    [Hint(PresentationHint.Script)]
    [Description("Email body (plain text or HTML). JS expression.")]
    public Script? Body { get; set; }

    /// <summary>
    /// 是否以 HTML 格式发送正文。默认 false（纯文本）。
    /// </summary>
    [Description("True to send body as HTML; false for plain text. Default false.")]
    public bool IsHtml { get; set; }

    /// <summary>
    /// 发件人地址。省略时回退到凭据的 <c>user</c> 字段。
    /// </summary>
    [Description("Sender address. When omitted, falls back to the credential's user field.")]
    public string? From { get; set; }

    /// <summary>
    /// 可选附件列表：逗号分隔的输入 JSON 字段名，其 base64 内容作为邮件附件。
    /// </summary>
    [Description("Optional comma-separated list of input JSON field names whose base64 content becomes email attachments.")]
    public string? Attachments { get; set; }

    /// <summary>
    /// 指定时改用 SMTP SpecifiedPickupDirectory，将 <c>.eml</c> 写入该目录而非联网发送（离线/暂存/测试）。
    /// </summary>
    [Description("When set, uses SMTP SpecifiedPickupDirectory to write .eml files here instead of network send (offline/staging/testing).")]
    public string? PickupDirectory { get; set; }

    /// <inheritdoc />
    public override async Task<NodeHandlerOutput> ExecuteAsync(NodeInput input, CancellationToken ct)
    {
        try
        {
            if (Connection is null)
            {
                throw new NodeExecutionException(FlowConstants.ErrorCodes.MissingConnection, "SMTP connection credential is required.");
            }

            if (!Connection.Fields.TryGetValue("host", out var host) || string.IsNullOrWhiteSpace(host))
            {
                throw new NodeExecutionException(FlowConstants.ErrorCodes.MissingConnection, "SMTP host is required.");
            }

            var useSsl = ParseBoolFlag(Connection.Fields.GetValueOrDefault("useSsl"));
            var port = ParsePort(Connection.Fields.GetValueOrDefault("port"), useSsl);
            var user = Connection.Fields.GetValueOrDefault("user");
            var password = Connection.Fields.GetValueOrDefault("password");

            var inputBatch = input.InputBatch;
            var firstItem = inputBatch.Items.Count > 0 ? inputBatch.Items[0].Data : null;

            var (to, toError) = await ResolveRequiredAsync(To, "To", "MissingTo", firstItem, ct).ConfigureAwait(false);
            if (toError is not null) return toError;

            var (subject, subjectError) = await ResolveRequiredAsync(Subject, "Subject", "MissingSubject", firstItem, ct).ConfigureAwait(false);
            if (subjectError is not null) return subjectError;

            var (body, bodyError) = await ResolveRequiredAsync(Body, "Body", "MissingBody", firstItem, ct).ConfigureAwait(false);
            if (bodyError is not null) return bodyError;

            var recipients = ParseRecipients(to);
            if (recipients.Count == 0)
            {
                throw new NodeExecutionException("MissingTo", "At least one recipient address is required.");
            }

            // 发件人：优先参数 From，否则凭据 user；两者皆空 → MissingFrom。
            var from = !string.IsNullOrWhiteSpace(From) ? From! : (user ?? string.Empty);
            if (string.IsNullOrWhiteSpace(from))
            {
                throw new NodeExecutionException("MissingFrom", "Sender address is required (set From or the credential's user field).");
            }

            using var message = new MailMessage
            {
                From = new MailAddress(from),
                Subject = subject!,
                Body = body!,
                IsBodyHtml = IsHtml
            };

            foreach (var recipient in recipients)
            {
                message.To.Add(recipient);
            }

            var attachmentError = TryAddAttachments(message, Attachments, firstItem);
            if (attachmentError is not null) return attachmentError;

            using var client = new SmtpClient
            {
                Host = host!,
                Port = port,
                EnableSsl = useSsl,
                DeliveryMethod = SmtpDeliveryMethod.Network
            };

            if (!string.IsNullOrWhiteSpace(user) && !string.IsNullOrWhiteSpace(password))
            {
                client.Credentials = new NetworkCredential(user, password);
            }

            // PickupDirectory：离线/暂存投递，将 .eml 写入磁盘而非联网。
            if (!string.IsNullOrWhiteSpace(PickupDirectory))
            {
                Directory.CreateDirectory(PickupDirectory);
                client.DeliveryMethod = SmtpDeliveryMethod.SpecifiedPickupDirectory;
                client.PickupDirectoryLocation = PickupDirectory;
            }

            await client.SendMailAsync(message, ct).ConfigureAwait(false);

            // 仅记录收件人数量与主题，绝不记录密码或完整凭据。
            Logger?.LogInformation("sendEmail 已发送邮件：收件人 {RecipientCount} 个，主题 {Subject}。", recipients.Count, subject);

            return NodeHandlerOutput.Data(new DataBatch
            {
                Items =
                [
                    new DataItem
                    {
                        Data = new JsonObject
                        {
                            ["success"] = true,
                            ["to"] = to,
                            ["subject"] = subject
                        },
                        Success = true,
                        SourceIndex = 0
                    }
                ]
            });
        }
        catch (OperationCanceledException)
        {
            throw new NodeExecutionException(FlowConstants.ErrorCodes.Cancelled, "sendEmail was cancelled.");
        }
        catch (ScriptErrorException ex)
        {
            throw new NodeExecutionException(FlowConstants.ErrorCodes.ScriptError, $"Expression evaluation failed: {ex.Message}");
        }
        catch (SmtpException ex)
        {
            // 仅记录非敏感信息（主题），不记录收件人明细/凭据/密码。
            Logger?.LogError(ex, "sendEmail SMTP 发送失败（主题 {Subject}）。", Subject is not null ? "(set)" : "(empty)");
            throw new NodeExecutionException("SmtpError", $"SMTP send failed: {ex.Message}");
        }
        catch (Exception ex) when (ex is not NodeExecutionException)
        {
            throw new NodeExecutionException(FlowConstants.ErrorCodes.UnexpectedError, $"Unexpected error sending email: {ex.Message}");
        }
    }

    /// <summary>
    /// 求值必填脚本表达式；脚本为 null 或求值结果为空白 → 返回对应错误。
    /// </summary>
    private async Task<(string? Value, NodeHandlerOutput? Error)> ResolveRequiredAsync(
        Script? script,
        string paramName,
        string errorCode,
        JsonNode? scope,
        CancellationToken cancellationToken)
    {
        if (script is null)
        {
            return (null, NodeHandlerOutput.Failure(errorCode, $"{paramName} is required."));
        }

        var value = await script.EvaluateAsync<string>(Ctx, item: scope, itemIndex: 0, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(value))
        {
            return (null, NodeHandlerOutput.Failure(errorCode, $"{paramName} is required."));
        }

        return (value, null);
    }

    /// <summary>
    /// 按逗号 / 分号 / 换行分隔并 trim，跳过空段，返回有效收件人地址列表。
    /// </summary>
    private static List<string> ParseRecipients(string? to)
    {
        var recipients = new List<string>();
        if (string.IsNullOrWhiteSpace(to)) return recipients;

        foreach (var part in to!.Split([';', ',', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!string.IsNullOrWhiteSpace(part)) recipients.Add(part);
        }

        return recipients;
    }

    /// <summary>
    /// 将附件字段（逗号分隔的输入字段名）的 base64 内容加入邮件；缺失字段或非法 base64 → InvalidAttachment。
    /// </summary>
    private static NodeHandlerOutput? TryAddAttachments(
        MailMessage message,
        string? attachments,
        JsonNode? firstItem)
    {
        if (string.IsNullOrWhiteSpace(attachments)) return null;

        foreach (var fieldName in attachments!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            // Read base64 from the attachment field and decode; missing or invalid base64 falls back to InvalidAttachment.
            var status = NodeDataHelpers.TryGetBase64Field(firstItem, fieldName, out var bytes);
            if (status is not NodeDataHelpers.Base64FieldResult.Success)
            {
                return NodeHandlerOutput.Failure("InvalidAttachment",
                    status == NodeDataHelpers.Base64FieldResult.Invalid
                        ? $"Attachment field '{fieldName}' is not valid base64."
                        : $"Attachment field '{fieldName}' is missing or not a base64 string.");
            }

            // MemoryStream is released by Attachment on Dispose (together with MailMessage).
            message.Attachments.Add(new Attachment(new MemoryStream(bytes), fieldName));
        }

        return null;
    }

    /// <summary>
    /// 解析 SMTP 端口：字段存在且为合法正整数时使用；否则 useSsl=true 默认 587，否则 25。
    /// </summary>
    private static int ParsePort(string? portStr, bool useSsl)
    {
        if (!string.IsNullOrWhiteSpace(portStr)
            && int.TryParse(portStr, out var parsed)
            && parsed > 0)
        {
            return parsed;
        }

        return useSsl ? 587 : 25;
    }

    /// <summary>
    /// 解析布尔型标志（useSsl 等）："true"/"1"/"yes"（不区分大小写）为 true，其余为 false。
    /// </summary>
    private static bool ParseBoolFlag(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;

        var normalized = value!.Trim().ToLowerInvariant();
        return normalized is "true" or "1" or "yes";
    }
}
using System.Net.Mail;
using System.Text;
using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Scripting;
using FlowEngine.Plugins.Standard;

namespace FlowEngine.Runtime.Tests.Plugins;

/// <summary>
/// sendEmail 节点测试：覆盖 PickupDirectory 成功投递、多收件人、HTML、缺连接/收件人/发件人、
/// 网络发送失败、base64 附件等路径。Connection 直接用 <see cref="CredentialValue"/> 构造（参考 DbReadNodeTests.ResolvedConnection），
/// PickupDirectory 指向临时目录下唯一子目录。
/// </summary>
public sealed class SendEmailNodeTests
{
    [Fact]
    public async Task ExecuteAsync_PickupDirectory_WritesEml_WithToSubjectBody()
    {
        var pickup = GetTempPickup();
        var node = new SendEmailNode
        {
            Connection = SmtpCredential("smtp.example.com", user: "sender@example.com"),
            To = Lit("a@example.com"),
            Subject = Lit("Hello"),
            Body = Lit("Hello body"),
            PickupDirectory = pickup
        };

        var result = await ((INodeType)node).ExecuteAsync(CreateContext(new DataBatch()), CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Equal("a@example.com", GetString(result.Output.Items[0].Data, "to"));
        Assert.Equal("Hello", GetString(result.Output.Items[0].Data, "subject"));
        Assert.True(GetBool(result.Output.Items[0].Data, "success"));

        var eml = ReadSingleEml(pickup);
        Assert.Contains("a@example.com", eml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Hello", eml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Hello body", ExtractBody(eml), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_MultipleRecipients_ParsedIntoTo()
    {
        var pickup = GetTempPickup();
        var node = new SendEmailNode
        {
            Connection = SmtpCredential("smtp.example.com", user: "sender@example.com"),
            To = Lit("a@x.com; b@y.com\r\nc@z.com"),
            Subject = Lit("Multi"),
            Body = Lit("body"),
            PickupDirectory = pickup
        };

        var result = await ((INodeType)node).ExecuteAsync(CreateContext(new DataBatch()), CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        var eml = ReadSingleEml(pickup);
        Assert.Contains("a@x.com", eml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("b@y.com", eml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("c@z.com", eml, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_IsHtmlTrue_EmitsHtmlContentType()
    {
        var pickup = GetTempPickup();
        var node = new SendEmailNode
        {
            Connection = SmtpCredential("smtp.example.com", user: "sender@example.com"),
            To = Lit("a@example.com"),
            Subject = Lit("Html"),
            Body = Lit("<h1>Hi</h1>"),
            IsHtml = true,
            PickupDirectory = pickup
        };

        var result = await ((INodeType)node).ExecuteAsync(CreateContext(new DataBatch()), CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        var eml = ReadSingleEml(pickup);
        Assert.Contains("text/html", eml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<h1>Hi</h1>", ExtractBody(eml), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_MissingConnection_ReturnsMissingConnection()
    {
        var node = new SendEmailNode
        {
            To = Lit("a@example.com"),
            Subject = Lit("S"),
            Body = Lit("B")
        };

        var result = await ((INodeType)node).ExecuteAsync(CreateContext(new DataBatch()), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(FlowConstants.ErrorCodes.MissingConnection, result.Error?.Code);
    }

    [Fact]
    public async Task ExecuteAsync_MissingTo_ReturnsMissingTo()
    {
        var node = new SendEmailNode
        {
            Connection = SmtpCredential("smtp.example.com", user: "sender@example.com"),
            To = Lit(""),
            Subject = Lit("S"),
            Body = Lit("B")
        };

        var result = await ((INodeType)node).ExecuteAsync(CreateContext(new DataBatch()), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("MissingTo", result.Error?.Code);
    }

    [Fact]
    public async Task ExecuteAsync_MissingFrom_ReturnsMissingFrom()
    {
        // From 省略，凭据 user 也为空 → MissingFrom。
        var node = new SendEmailNode
        {
            Connection = SmtpCredential("smtp.example.com", user: ""),
            To = Lit("a@example.com"),
            Subject = Lit("S"),
            Body = Lit("B")
        };

        var result = await ((INodeType)node).ExecuteAsync(CreateContext(new DataBatch()), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("MissingFrom", result.Error?.Code);
    }

    [Fact]
    public async Task ExecuteAsync_NetworkSendFailure_ReturnsSmtpErrorNotThrown()
    {
        // 无 PickupDirectory，指向不可达的回环端口（127.0.0.1:1 无监听 → 连接被拒，快速失败）。
        var node = new SendEmailNode
        {
            Connection = SmtpCredential("127.0.0.1", port: "1", user: "sender@example.com"),
            To = Lit("a@example.com"),
            Subject = Lit("S"),
            Body = Lit("B")
        };

        var result = await ((INodeType)node).ExecuteAsync(CreateContext(new DataBatch()), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("SmtpError", result.Error?.Code);
    }

    [Fact]
    public async Task ExecuteAsync_AttachmentFromBase64_Succeeds()
    {
        var pickup = GetTempPickup();
        var payload = Encoding.UTF8.GetBytes("attachment-content");
        var node = new SendEmailNode
        {
            Connection = SmtpCredential("smtp.example.com", user: "sender@example.com"),
            To = Lit("a@example.com"),
            Subject = Lit("S"),
            Body = Lit("B"),
            Attachments = "file1",
            PickupDirectory = pickup
        };

        var input = new DataBatch
        {
            Items =
            [
                new DataItem
                {
                    Data = new JsonObject { ["file1"] = Convert.ToBase64String(payload) },
                    Success = true
                }
            ]
        };

        var result = await ((INodeType)node).ExecuteAsync(CreateContext(input), CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Single(Directory.GetFiles(pickup, "*.eml"));
    }

    [Fact]
    public async Task ExecuteAsync_AttachmentMissingBase64_ReturnsInvalidAttachment()
    {
        var node = new SendEmailNode
        {
            Connection = SmtpCredential("smtp.example.com", user: "sender@example.com"),
            To = Lit("a@example.com"),
            Subject = Lit("S"),
            Body = Lit("B"),
            Attachments = "file1",
            PickupDirectory = GetTempPickup()
        };

        var input = new DataBatch
        {
            Items =
            [
                new DataItem
                {
                    Data = new JsonObject { ["file1"] = "not-valid-base64-!!!" },
                    Success = true
                }
            ]
        };

        var result = await ((INodeType)node).ExecuteAsync(CreateContext(input), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("InvalidAttachment", result.Error?.Code);
    }

    private static bool GetBool(JsonNode? node, string key)
        => node?[key] is JsonValue value && value.TryGetValue<bool>(out var b) && b;

    private static string? GetString(JsonNode? node, string key)
        => node?[key] is JsonValue value ? value.GetValue<string>() : null;

    private static Script Lit(string s)
    {
        // 转义为合法 JS 字符串字面量（含换行 \r\n → 转义序列）。
        var escaped = s
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n");
        return (Script)$"\"{escaped}\"";
    }

    private static CredentialValue SmtpCredential(string host, string? port = null, string? user = null)
    {
        var fields = new Dictionary<string, string>();
        if (host is not null) fields["host"] = host;
        if (port is not null) fields["port"] = port;
        if (user is not null) fields["user"] = user;
        fields["password"] = "secret-password"; // 测试用占位，绝不输出到日志/异常
        return new CredentialValue
        {
            Name = "test-smtp",
            Type = "smtp",
            Fields = fields
        };
    }

    private static string GetTempPickup()
    {
        var dir = Path.Combine(Path.GetTempPath(), "flow-engine-sendemail-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string ReadSingleEml(string pickup)
    {
        var files = Directory.GetFiles(pickup, "*.eml");
        Assert.Single(files);
        return File.ReadAllText(files[0]);
    }

    /// <summary>
    /// 从 .eml 文本中取出正文（处理 base64 / quoted-printable 编码），用于断言 Body 内容。
    /// </summary>
    private static string ExtractBody(string eml)
    {
        var sep = eml.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        if (sep < 0) sep = eml.IndexOf("\n\n", StringComparison.Ordinal);
        var body = sep >= 0 ? eml.Substring(sep).TrimStart('\r', '\n') : eml;

        var encoding = string.Empty;
        foreach (var line in eml.Split('\n'))
        {
            if (line.StartsWith("Content-Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
            {
                var eq = line.IndexOf(':');
                if (eq >= 0) encoding = line.Substring(eq + 1).Trim().ToLowerInvariant();
            }
        }

        if (encoding.Contains("base64"))
        {
            var b64 = body.Replace("\r", string.Empty).Replace("\n", string.Empty).Replace(" ", string.Empty);
            return Encoding.UTF8.GetString(Convert.FromBase64String(b64));
        }

        if (encoding.Contains("quoted-printable"))
        {
            return body
                .Replace("=\r\n", string.Empty)
                .Replace("=\n", string.Empty);
        }

        return body;
    }

    private static NodeExecutionContext CreateContext(DataBatch input)
    {
        return new NodeExecutionContext
        {
            Workflow = new Workflow { Id = Guid.NewGuid(), Name = "test" },
            ExecutionId = Guid.NewGuid(),
            Node = new NodeDefinition
            {
                Id = "sendEmail",
                TypeName = "sendEmail",
                Name = "sendEmail"
            },
            Inputs = new Dictionary<string, DataBatch>(StringComparer.OrdinalIgnoreCase)
            {
                [FlowConstants.PortNames.Input] = input
            }
        };
    }
}

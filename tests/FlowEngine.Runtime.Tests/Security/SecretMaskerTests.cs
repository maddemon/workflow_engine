using System.Text.Json.Nodes;
using FlowEngine.Core.Entities;
using FlowEngine.Runtime.Security;

namespace FlowEngine.Runtime.Tests.Security;

public sealed class SecretMaskerTests
{
    private readonly ISecretMasker _masker = new SecretMasker();
    private static readonly IReadOnlySet<string> Sensitive = new HashSet<string> { "top-secret" };

    [Fact]
    public void MaskValue_CredentialValue_MaskedToNameTypeOnly()
    {
        var cred = new CredentialValue { Name = "my-key", Type = "apiKey", Fields = new() { ["key"] = "top-secret" } };

        var result = Assert.IsType<Dictionary<string, object>>(_masker.MaskValue(cred, Sensitive));

        Assert.Equal("my-key", result["name"]);
        Assert.Equal("apiKey", result["type"]);
        Assert.False(result.ContainsKey("Fields"));
        Assert.False(result.ContainsKey("fields"));
    }

    [Fact]
    public void MaskValue_StringInSensitiveSet_Masked()
    {
        Assert.Equal("***", _masker.MaskValue("top-secret", Sensitive));
    }

    [Fact]
    public void MaskValue_StringNotSensitive_Unchanged()
    {
        Assert.Equal("hello", _masker.MaskValue("hello", Sensitive));
    }

    [Fact]
    public void MaskValue_NestedDictionaryWithCredentialValue_Masked()
    {
        var inner = new CredentialValue { Name = "k", Type = "t", Fields = new() { ["x"] = "y" } };
        var dict = new Dictionary<string, object> { ["cred"] = inner, ["plain"] = "top-secret" };

        var result = Assert.IsType<Dictionary<string, object?>>(_masker.MaskValue(dict, Sensitive));
        var maskedCred = Assert.IsType<Dictionary<string, object>>(result["cred"]);

        Assert.Equal("k", maskedCred["name"]);
        Assert.Equal("***", result["plain"]);
    }

    [Fact]
    public void MaskValue_JsonNodeWithSensitiveLiteral_Masked()
    {
        var node = JsonNode.Parse("\"top-secret\"");

        var result = Assert.IsAssignableFrom<JsonValue>(_masker.MaskValue(node!, Sensitive));

        Assert.Equal("***", result.GetValue<string>());
    }

    [Fact]
    public void MaskDataBatch_MasksItems()
    {
        var batch = new DataBatch
        {
            Items =
            [
                new DataItem { Data = JsonNode.Parse("\"top-secret\""), Success = true },
                new DataItem { Data = JsonNode.Parse("\"ok\""), Success = true }
            ]
        };

        var result = _masker.MaskDataBatch(batch, Sensitive);

        Assert.Equal("***", result.Items[0].Data!.GetValue<string>());
        Assert.Equal("ok", result.Items[1].Data!.GetValue<string>());
    }

    [Fact]
    public void MaskOutput_MasksOutputBatch()
    {
        var output = new NodeExecutionResult
        {
            Success = true,
            Output = new DataBatch { Items = [new DataItem { Data = JsonNode.Parse("\"top-secret\""), Success = true }] }
        };

        var result = _masker.MaskOutput(output, Sensitive);

        Assert.Equal("***", result.Output.Items[0].Data!.GetValue<string>());
    }

    [Fact]
    public void MaskParameters_EmptySensitiveSet_StillMasksCredentialValue()
    {
        var cred = new CredentialValue { Name = "k", Type = "t", Fields = new() { ["x"] = "y" } };
        var parameters = new Dictionary<string, object> { ["cred"] = cred };

        var result = _masker.MaskParameters(parameters, new HashSet<string>());
        var masked = Assert.IsType<Dictionary<string, object>>(result["cred"]);

        Assert.Equal("k", masked["name"]);
        Assert.False(masked.ContainsKey("Fields"));
    }
}

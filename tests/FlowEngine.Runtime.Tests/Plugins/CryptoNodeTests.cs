using System.Text.Json.Nodes;
using FlowEngine.Core.Entities;
using FlowEngine.Plugins.Standard;
using Xunit;

namespace FlowEngine.Runtime.Tests.Plugins;

/// <summary>
/// CryptoNode（N04）测试：覆盖哈希 / Base64 / AES / HMAC 的轮转（round-trip）与错误路径。
/// 沿用 DateTimeNodeTests 的模式：用空实例构建上下文，再执行独立配置的节点，避免参数水合覆盖手动属性。
/// </summary>
public sealed class CryptoNodeTests
{
    private const string Sample = "Hello, Crypto!";

    [Fact]
    public async Task Base64EncodeDecode_RoundTrip_Equal()
    {
        var encoded = await RunAsync(new CryptoNode
        {
            Operation = CryptoOperation.Base64Encode,
            Input = $"\"{Sample}\""
        });
        Assert.True(encoded.Success);
        var encodedValue = Value(encoded);

        var decoded = await RunAsync(new CryptoNode
        {
            Operation = CryptoOperation.Base64Decode,
            Input = $"\"{encodedValue}\""
        });

        Assert.True(decoded.Success);
        Assert.Equal(Sample, Value(decoded));
    }

    [Fact]
    public async Task AesEncryptDecrypt_RoundTrip_Equal()
    {
        const string key = "a-strong-passphrase";
        var encrypted = await RunAsync(new CryptoNode
        {
            Operation = CryptoOperation.AesEncrypt,
            Input = $"\"{Sample}\"",
            Key = key
        });
        Assert.True(encrypted.Success);
        var cipher = Value(encrypted);

        var decrypted = await RunAsync(new CryptoNode
        {
            Operation = CryptoOperation.AesDecrypt,
            Input = $"\"{cipher}\"",
            Key = key
        });

        Assert.True(decrypted.Success);
        Assert.Equal(Sample, Value(decrypted));
    }

    [Fact]
    public async Task AesEncryptDecrypt_HexOutput_RoundTrip_Equal()
    {
        const string key = "a-strong-passphrase";
        var encrypted = await RunAsync(new CryptoNode
        {
            Operation = CryptoOperation.AesEncrypt,
            Input = $"\"{Sample}\"",
            Key = key,
            OutputEncoding = CryptoOutputEncoding.Hex
        });
        Assert.True(encrypted.Success);
        var cipher = Value(encrypted);
        Assert.All(cipher, c => Assert.True("0123456789abcdef".Contains(c)));

        var decrypted = await RunAsync(new CryptoNode
        {
            Operation = CryptoOperation.AesDecrypt,
            Input = $"\"{cipher}\"",
            Key = key,
            OutputEncoding = CryptoOutputEncoding.Hex
        });

        Assert.True(decrypted.Success);
        Assert.Equal(Sample, Value(decrypted));
    }

    [Fact]
    public async Task HmacSign_Deterministic()
    {
        const string key = "signing-key";
        var a = await RunAsync(new CryptoNode
        {
            Operation = CryptoOperation.HmacSign,
            Input = $"\"{Sample}\"",
            Key = key
        });
        var b = await RunAsync(new CryptoNode
        {
            Operation = CryptoOperation.HmacSign,
            Input = $"\"{Sample}\"",
            Key = key
        });

        Assert.True(a.Success);
        Assert.True(b.Success);
        Assert.Equal(Value(a), Value(b));
    }

    [Fact]
    public async Task Hash_Deterministic()
    {
        var a = await RunAsync(new CryptoNode
        {
            Operation = CryptoOperation.Hash,
            Algorithm = CryptoAlgorithm.SHA256,
            Input = $"\"{Sample}\""
        });
        var b = await RunAsync(new CryptoNode
        {
            Operation = CryptoOperation.Hash,
            Algorithm = CryptoAlgorithm.SHA256,
            Input = $"\"{Sample}\""
        });

        Assert.True(a.Success);
        Assert.True(b.Success);
        Assert.Equal(Value(a), Value(b));
        // SHA-256 十六进制长度 = 64
        Assert.Equal(64, Value(a).Length);
    }

    [Fact]
    public async Task AesDecrypt_WrongKey_ReturnsError()
    {
        const string key = "correct-key";
        var encrypted = await RunAsync(new CryptoNode
        {
            Operation = CryptoOperation.AesEncrypt,
            Input = $"\"{Sample}\"",
            Key = key
        });
        Assert.True(encrypted.Success);
        var cipher = Value(encrypted);

        var decrypted = await RunAsync(new CryptoNode
        {
            Operation = CryptoOperation.AesDecrypt,
            Input = $"\"{cipher}\"",
            Key = "wrong-key"
        });

        Assert.False(decrypted.Success);
        Assert.Equal("DecryptFailed", decrypted.Error?.Code);
    }

    [Fact]
    public async Task AesEncrypt_MissingKey_ReturnsError()
    {
        var result = await RunAsync(new CryptoNode
        {
            Operation = CryptoOperation.AesEncrypt,
            Input = $"\"{Sample}\""
        });

        Assert.False(result.Success);
        Assert.Equal("MissingKey", result.Error?.Code);
    }

    [Fact]
    public async Task HmacSign_MissingKey_ReturnsError()
    {
        var result = await RunAsync(new CryptoNode
        {
            Operation = CryptoOperation.HmacSign,
            Input = $"\"{Sample}\""
        });

        Assert.False(result.Success);
        Assert.Equal("MissingKey", result.Error?.Code);
    }

    [Fact]
    public async Task UnknownOperation_ReturnsError()
    {
        var result = await RunAsync(new CryptoNode
        {
            Operation = (CryptoOperation)999,
            Input = $"\"{Sample}\""
        });

        Assert.False(result.Success);
        Assert.Equal("UnknownOperation", result.Error?.Code);
    }

    [Fact]
    public async Task Hash_UnsupportedAlgorithm_ReturnsError()
    {
        var result = await RunAsync(new CryptoNode
        {
            Operation = CryptoOperation.Hash,
            Algorithm = CryptoAlgorithm.AES,
            Input = $"\"{Sample}\""
        });

        Assert.False(result.Success);
        Assert.Equal("UnsupportedAlgorithm", result.Error?.Code);
    }

    [Fact]
    public async Task Base64Decode_InvalidInput_ReturnsError()
    {
        var result = await RunAsync(new CryptoNode
        {
            Operation = CryptoOperation.Base64Decode,
            Input = "\"not!valid!base64\""
        });

        Assert.False(result.Success);
        Assert.Equal("InvalidInput", result.Error?.Code);
    }

    private static async Task<NodeExecutionResult> RunAsync(CryptoNode node)
    {
        var context = await NodeTestContextFactory.BuildAsync(new CryptoNode(), new Dictionary<string, object>());
        return await node.ExecuteAsync(context, CancellationToken.None);
    }

    private static string Value(NodeExecutionResult result)
        => Assert.IsType<JsonObject>(result.Output.Items[0].Data)["value"]!.GetValue<string>();
}

using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
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
/// 加密 / 解密节点，提供哈希、Base64 编解码、AES 对称加密与 HMAC 签名等纯文本运算。
/// 全部基于 BCL（<see cref="System.Security.Cryptography"/>），不引入第三方加密库。
/// 输出为单条 <c>DataItem</c>，字段含 <c>value</c>（运算结果字符串）。
/// </summary>
/// <remarks>
/// <para>密钥派生：AES 与 HMAC 的密钥由 <c>Key</c> 字符串经 SHA-256 派生为 32 字节（AES-256 / HMAC-SHA256），
/// 以兼顾任意长度口令并避免要求用户精确提供 16/24/32 字节。该派生方式为确定性、可复现的，便于轮转与测试。</para>
/// <para>弱算法：<c>MD5</c> / <c>SHA1</c> 仍可实现（哈希校验等非安全场景可用），但文档标注不推荐用于安全敏感场景。</para>
/// <para>安全：绝不向日志、异常消息或任何输出写入 <c>Key</c> / 凭据内容。</para>
/// </remarks>
[NodeMeta(TypeName = "crypto", DisplayName = "Crypto", Category = NodeCategory.Data, Icon = "lock", DefaultIsEntry = false)]
[Port(FlowConstants.PortNames.Input, "Input", PortDirection.Input, PortType.Main)]
[Port(FlowConstants.PortNames.Output, "Output", PortDirection.Output, PortType.Main)]
public sealed class CryptoNode : NodeBase
{
    [Inject] public NodeExecutionContext Ctx { get; private set; } = null!;

    /// <summary>AES-GCM 随机 Nonce 长度（字节）。</summary>
    private const int NonceSize = 12;

    /// <summary>AES-GCM 认证标签长度（字节）。</summary>
    private const int TagSize = 16;

    /// <summary>
    /// 运算类型：hash | base64Encode | base64Decode | aesEncrypt | aesDecrypt | hmacSign。
    /// </summary>
    [Description("Operation: hash | base64Encode | base64Decode | aesEncrypt | aesDecrypt | hmacSign.")]
    public CryptoOperation Operation { get; set; } = CryptoOperation.Hash;

    /// <summary>
    /// 输入文本（支持 JS 表达式，如 <c>$json.text</c>；纯文本字面量亦可直接填写）。
    /// 对 base64Decode / aesDecrypt 为待解码 / 待解密内容；对其余运算为待处理文本。
    /// </summary>
    [Hint(PresentationHint.Script)]
    [Description("Input text (supports JS expressions e.g. $json.text; plain literals allowed). For base64Decode/aesDecrypt this is the encoded/encrypted payload.")]
    public Script Input { get; set; } = Script.Empty;

    /// <summary>
    /// 算法：哈希运算可选 SHA256 | SHA1 | MD5；AES / HMAC 运算固定使用 AES-256 / HMAC-SHA256（此参数仅对哈希有意义）。
    /// </summary>
    [Description("Algorithm: for hash use SHA256 | SHA1 | MD5; for aes/hmac it is fixed to AES-256 / HMAC-SHA256 (ignored there).")]
    public CryptoAlgorithm Algorithm { get; set; } = CryptoAlgorithm.SHA256;

    /// <summary>
    /// 对称密钥（aesEncrypt / aesDecrypt / hmacSign 必填）。经 SHA-256 派生为 32 字节密钥，绝不写入日志。
    /// </summary>
    [Description("Key for aesEncrypt/aesDecrypt/hmacSign (required). Derived to a 32-byte key via SHA-256; never logged.")]
    public string? Key { get; set; }

    /// <summary>
    /// 输入 / 输出文本编码，默认 UTF-8。
    /// </summary>
    [Description("Text encoding for input/output. Defaults to UTF-8.")]
    public CryptoEncoding Encoding { get; set; } = CryptoEncoding.Utf8;

    /// <summary>
    /// 二进制输出的字符串表示：base64（默认）或 hex。作用于 aesEncrypt / hmacSign 的输出；哈希与 base64 运算不受影响。
    /// </summary>
    [Description("Output representation for binary results (aesEncrypt/hmacSign): base64 (default) or hex.")]
    public CryptoOutputEncoding OutputEncoding { get; set; } = CryptoOutputEncoding.Base64;

    /// <inheritdoc />
    public override async Task<NodeHandlerOutput> ExecuteAsync(NodeInput input, CancellationToken ct)
    {
        try
        {
            var encoding = ResolveEncoding();

            var inputBatch = input.InputBatch;
            var items = inputBatch.Items.Count == 0
                ? new List<DataItem> { new() { Data = null, SourceIndex = 0 } }
                : inputBatch.Items;

            var outputItems = new List<DataItem>();
            foreach (var item in items)
            {
                var text = await ResolveInputTextAsync(item.Data, item.SourceIndex, ct)
                    .ConfigureAwait(false);

                var result = Process(text ?? string.Empty, encoding);
                if (result.Error is not null)
                {
                    return result;
                }

                outputItems.Add(result.Batch.Items[0]);
            }

            return NodeHandlerOutput.Data(new DataBatch { Items = outputItems });
        }
        catch (OperationCanceledException)
        {
            throw new NodeExecutionException(FlowConstants.ErrorCodes.Cancelled, "Crypto operation was cancelled.");
        }
        catch (Exception ex) when (ex is not NodeExecutionException)
        {
            // 异常消息中不得包含 Key；仅描述操作失败。
            throw new NodeExecutionException(FlowConstants.ErrorCodes.UnexpectedError, $"Crypto error: {ex.Message}");
        }
    }

    /// <summary>
    /// 按 <see cref="Operation"/> 分发到具体运算；返回单条 <c>DataItem</c> 的成功结果或失败输出。
    /// </summary>
    private NodeHandlerOutput Process(string text, Encoding encoding)
    {
        return Operation switch
        {
            CryptoOperation.Hash => HandleHash(text, encoding),
            CryptoOperation.Base64Encode => HandleBase64Encode(text, encoding),
            CryptoOperation.Base64Decode => HandleBase64Decode(text, encoding),
            CryptoOperation.AesEncrypt => HandleAesEncrypt(text, encoding),
            CryptoOperation.AesDecrypt => HandleAesDecrypt(text, encoding),
            CryptoOperation.HmacSign => HandleHmacSign(text, encoding),
            _ => NodeHandlerOutput.Failure("UnknownOperation", $"Unsupported Operation '{Operation}'.")
        };
    }

    /// <summary>hash：对输入字节计算摘要，返回小写十六进制字符串。</summary>
    private NodeHandlerOutput HandleHash(string text, Encoding encoding)
    {
        if (Algorithm is not (CryptoAlgorithm.SHA256 or CryptoAlgorithm.SHA1 or CryptoAlgorithm.MD5))
        {
            return NodeHandlerOutput.Failure("UnsupportedAlgorithm", $"Algorithm '{Algorithm}' is not valid for hash (use SHA256 | SHA1 | MD5).");
        }

        var bytes = encoding.GetBytes(text);
        var digest = Algorithm switch
        {
            CryptoAlgorithm.SHA1 => SHA1.HashData(bytes),
            CryptoAlgorithm.MD5 => MD5.HashData(bytes),
            _ => SHA256.HashData(bytes)
        };

        return NodeHandlerOutput.Data(new DataBatch
        {
            Items =
            [
                new DataItem
                {
                    Data = new JsonObject { ["value"] = ToHex(digest) },
                    Success = true,
                    SourceIndex = 0
                }
            ]
        });
    }

    /// <summary>base64Encode：将输入文本按编码转为字节再做 Base64 编码。</summary>
    private static NodeHandlerOutput HandleBase64Encode(string text, Encoding encoding)
    {
        var encoded = Convert.ToBase64String(encoding.GetBytes(text));
        return NodeHandlerOutput.Data(new DataBatch
        {
            Items =
            [
                new DataItem
                {
                    Data = new JsonObject { ["value"] = encoded },
                    Success = true,
                    SourceIndex = 0
                }
            ]
        });
    }

    /// <summary>base64Decode：Base64 解码为字节，再按编码还原为文本。非法 Base64 返回错误结果。</summary>
    private static NodeHandlerOutput HandleBase64Decode(string text, Encoding encoding)
    {
        try
        {
            var bytes = Convert.FromBase64String(text);
            return NodeHandlerOutput.Data(new DataBatch
            {
                Items =
                [
                    new DataItem
                    {
                        Data = new JsonObject { ["value"] = encoding.GetString(bytes) },
                        Success = true,
                        SourceIndex = 0
                    }
                ]
            });
        }
        catch (FormatException)
        {
            return NodeHandlerOutput.Failure("InvalidInput", "Input is not valid Base64.");
        }
    }

    /// <summary>aesEncrypt：AES-GCM 加密，随机 12 字节 Nonce 与密文 + 标签拼接后按 <see cref="OutputEncoding"/> 编码输出。</summary>
    private NodeHandlerOutput HandleAesEncrypt(string text, Encoding encoding)
    {
        if (string.IsNullOrEmpty(Key))
        {
            return NodeHandlerOutput.Failure("MissingKey", "Key is required for aesEncrypt.");
        }

        var key = DeriveKey(Key!);
        try
        {
            using var aes = new AesGcm(key, TagSize);
            var nonce = RandomNumberGenerator.GetBytes(NonceSize);
            var plaintext = encoding.GetBytes(text);
            var ciphertext = new byte[plaintext.Length];
            var tag = new byte[TagSize];
            aes.Encrypt(nonce, plaintext, ciphertext, tag);

            var combined = new byte[nonce.Length + ciphertext.Length + tag.Length];
            Buffer.BlockCopy(nonce, 0, combined, 0, nonce.Length);
            Buffer.BlockCopy(ciphertext, 0, combined, nonce.Length, ciphertext.Length);
            Buffer.BlockCopy(tag, 0, combined, nonce.Length + ciphertext.Length, tag.Length);

            return NodeHandlerOutput.Data(new DataBatch
            {
                Items =
                [
                    new DataItem
                    {
                        Data = new JsonObject { ["value"] = EncodeBytes(combined, OutputEncoding) },
                        Success = true,
                        SourceIndex = 0
                    }
                ]
            });
        }
        catch (CryptographicException ex)
        {
            return NodeHandlerOutput.Failure("EncryptFailed", $"AES encryption failed: {ex.Message}");
        }
    }

    /// <summary>aesDecrypt：解析 Base64/hex 输入，拆分 Nonce/密文/标签后 AES-GCM 解密。密钥错误（标签不匹配）返回错误结果。</summary>
    private NodeHandlerOutput HandleAesDecrypt(string text, Encoding encoding)
    {
        if (string.IsNullOrEmpty(Key))
        {
            return NodeHandlerOutput.Failure("MissingKey", "Key is required for aesDecrypt.");
        }

        var combined = DecodeBytesOrThrow(text, OutputEncoding);

        if (combined.Length < NonceSize + TagSize)
        {
            return NodeHandlerOutput.Failure("InvalidInput", "Encrypted payload is too short.");
        }

        var key = DeriveKey(Key!);
        try
        {
            var nonce = new byte[NonceSize];
            var tag = new byte[TagSize];
            var ciphertext = new byte[combined.Length - NonceSize - TagSize];
            Buffer.BlockCopy(combined, 0, nonce, 0, NonceSize);
            Buffer.BlockCopy(combined, NonceSize, ciphertext, 0, ciphertext.Length);
            Buffer.BlockCopy(combined, NonceSize + ciphertext.Length, tag, 0, TagSize);

            var plaintext = new byte[ciphertext.Length];
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(nonce, ciphertext, tag, plaintext);

            return NodeHandlerOutput.Data(new DataBatch
            {
                Items =
                [
                    new DataItem
                    {
                        Data = new JsonObject { ["value"] = encoding.GetString(plaintext) },
                        Success = true,
                        SourceIndex = 0
                    }
                ]
            });
        }
        catch (CryptographicException)
        {
            // 标签不匹配：密钥错误或被篡改，不得泄露任何细节。
            return NodeHandlerOutput.Failure("DecryptFailed", "Decryption failed: invalid key or corrupted payload.");
        }
    }

    /// <summary>hmacSign：以派生密钥计算 HMAC-SHA256，结果按 <see cref="OutputEncoding"/> 编码输出。</summary>
    private NodeHandlerOutput HandleHmacSign(string text, Encoding encoding)
    {
        if (string.IsNullOrEmpty(Key))
        {
            return NodeHandlerOutput.Failure("MissingKey", "Key is required for hmacSign.");
        }

        var key = DeriveKey(Key!);
        try
        {
            using var hmac = new HMACSHA256(key);
            var signature = hmac.ComputeHash(encoding.GetBytes(text));
            return NodeHandlerOutput.Data(new DataBatch
            {
                Items =
                [
                    new DataItem
                    {
                        Data = new JsonObject { ["value"] = EncodeBytes(signature, OutputEncoding) },
                        Success = true,
                        SourceIndex = 0
                    }
                ]
            });
        }
        catch (CryptographicException ex)
        {
            return NodeHandlerOutput.Failure("SignFailed", $"HMAC signing failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 将 <c>rawKey</c> 经 SHA-256 派生为 32 字节密钥（AES-256 / HMAC-SHA256）。确定性、可复现。
    /// </summary>
    private static byte[] DeriveKey(string rawKey) => SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(rawKey));

    /// <summary>按 <see cref="OutputEncoding"/> 将字节编码为 base64（默认）或十六进制字符串。</summary>
    private static string EncodeBytes(byte[] data, CryptoOutputEncoding outputEncoding)
        => outputEncoding == CryptoOutputEncoding.Hex
            ? ToHex(data)
            : Convert.ToBase64String(data);

    /// <summary>解析 base64 或十六进制字符串为字节；格式非法时抛出 <see cref="NodeExecutionException"/>。</summary>
    private static byte[] DecodeBytesOrThrow(string text, CryptoOutputEncoding outputEncoding)
    {
        try
        {
            return outputEncoding == CryptoOutputEncoding.Hex
                ? FromHex(text)
                : Convert.FromBase64String(text);
        }
        catch (FormatException)
        {
            throw new NodeExecutionException("InvalidInput", "Input is not valid Base64/hex for the configured OutputEncoding.");
        }
    }

    /// <summary>将字节转换为小写十六进制字符串。</summary>
    private static string ToHex(byte[] data)
    {
        var sb = new StringBuilder(data.Length * 2);
        foreach (var b in data)
        {
            sb.Append(b.ToString("x2"));
        }

        return sb.ToString();
    }

    /// <summary>将十六进制字符串解析为字节；长度非偶数或含非法字符时抛 <see cref="FormatException"/>。</summary>
    private static byte[] FromHex(string hex)
    {
        if (hex.Length % 2 != 0)
        {
            throw new FormatException("Hex string must have an even length.");
        }

        var bytes = new byte[hex.Length / 2];
        for (var i = 0; i < bytes.Length; i++)
        {
            bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        }

        return bytes;
    }

    /// <summary>将 <see cref="Encoding"/> 枚举映射为 <see cref="System.Text.Encoding"/>。</summary>
    private Encoding ResolveEncoding() => Encoding switch
    {
        CryptoEncoding.Utf16 => System.Text.Encoding.Unicode,
        CryptoEncoding.Ascii => System.Text.Encoding.ASCII,
        CryptoEncoding.Latin1 => System.Text.Encoding.Latin1,
        _ => System.Text.Encoding.UTF8
    };

    /// <summary>
    /// 求值输入文本。表达式为 JS 表达式（如 <c>$json.text</c>）；若求值失败（如未加引号的字面量），
    /// 退化为将源文本作为字面量字符串，兼顾纯文本输入场景。
    /// </summary>
    private async Task<string?> ResolveInputTextAsync(
        JsonNode? item, int index, CancellationToken cancellationToken)
    {
        if (Input is null || string.IsNullOrEmpty(Input.Source))
        {
            return string.Empty;
        }

        try
        {
            return await Input.EvaluateAsync<string>(Ctx, item: item, itemIndex: index, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (ScriptErrorException)
        {
            // 表达式求值失败：回退为字面量，不向上抛出（遵循"主动中止而非抛异常"的契约）。
            return Input.Source;
        }
    }
}

/// <summary>
/// Crypto 节点的运算类型。
/// </summary>
public enum CryptoOperation
{
    /// <summary>计算输入的哈希摘要（小写十六进制）。</summary>
    [Description("Compute hash (digest) of the input as lowercase hex.")]
    Hash,

    /// <summary>将输入文本编码为 Base64。</summary>
    [Description("Encode input text as Base64.")]
    Base64Encode,

    /// <summary>将 Base64 输入解码为文本。</summary>
    [Description("Decode Base64 input back to text.")]
    Base64Decode,

    /// <summary>使用 AES-GCM 加密输入（密钥经 SHA-256 派生）。</summary>
    [Description("Encrypt input with AES-GCM (key derived via SHA-256).")]
    AesEncrypt,

    /// <summary>使用 AES-GCM 解密输入（密钥经 SHA-256 派生）。</summary>
    [Description("Decrypt AES-GCM input (key derived via SHA-256).")]
    AesDecrypt,

    /// <summary>计算输入的 HMAC-SHA256 签名。</summary>
    [Description("Compute HMAC-SHA256 signature of the input.")]
    HmacSign
}

/// <summary>
/// Crypto 节点的算法选择。哈希运算使用 SHA256 | SHA1 | MD5；AES / HMAC 运算固定为 AES-256 / HMAC-SHA256。
/// </summary>
public enum CryptoAlgorithm
{
    /// <summary>SHA-256（哈希，推荐）。</summary>
    [Description("SHA-256 (hash). Recommended.")]
    SHA256,

    /// <summary>SHA-1（哈希，弱算法，不推荐安全场景）。</summary>
    [Description("SHA-1 (hash, weak - avoid in secure contexts).")]
    SHA1,

    /// <summary>MD5（哈希，弱算法，不推荐安全场景）。</summary>
    [Description("MD5 (hash, weak - avoid in secure contexts).")]
    MD5,

    /// <summary>AES：用于 aes/hmac 运算（映射为 AES-256 / HMAC-SHA256），对哈希运算无意义。</summary>
    [Description("AES: used for aes/hmac (maps to AES-256 / HMAC-SHA256). Irrelevant for hash.")]
    AES
}

/// <summary>
/// Crypto 节点的文本编码选择（输入 / 输出的字节 ↔ 文本转换）。
/// </summary>
public enum CryptoEncoding
{
    /// <summary>UTF-8（默认，推荐）。</summary>
    [Description("UTF-8 (default).")]
    Utf8,

    /// <summary>UTF-16（小端）。</summary>
    [Description("UTF-16 (little-endian).")]
    Utf16,

    /// <summary>ASCII（7 位）。</summary>
    [Description("ASCII (7-bit).")]
    Ascii,

    /// <summary>Latin-1（ISO-8859-1）。</summary>
    [Description("Latin-1 (ISO-8859-1).")]
    Latin1
}

/// <summary>
/// Crypto 节点二进制结果的字符串表示：base64（默认）或十六进制。作用于 aesEncrypt / hmacSign 输出。
/// </summary>
public enum CryptoOutputEncoding
{
    /// <summary>Base64 字符串（默认）。</summary>
    [Description("Base64 string (default).")]
    Base64,

    /// <summary>小写十六进制字符串。</summary>
    [Description("Lowercase hexadecimal string.")]
    Hex
}

using System.Text;
using FlowEngine.Infrastructure.Security;
using Xunit;

namespace FlowEngine.Infrastructure.Tests.Security;

public sealed class CryptoKeyProviderTests : IDisposable
{
    private readonly string _keyFilePath;
    private string? _originalEnvironment;
    private string? _originalAspNetCoreEnvironment;
    private string? _originalDotNetEnvironment;

    public CryptoKeyProviderTests()
    {
        _keyFilePath = Path.Combine(Path.GetTempPath(), $"flowengine-crypto-{Guid.NewGuid():N}.key");
        _originalEnvironment = Environment.GetEnvironmentVariable("FLOWENGINE_CRYPTO_KEY");
        _originalAspNetCoreEnvironment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        _originalDotNetEnvironment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
    }

    public void Dispose()
    {
        SetEnvironmentVariable("FLOWENGINE_CRYPTO_KEY", _originalEnvironment);
        SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", _originalAspNetCoreEnvironment);
        SetEnvironmentVariable("DOTNET_ENVIRONMENT", _originalDotNetEnvironment);

        try
        {
            if (File.Exists(_keyFilePath))
            {
                File.Delete(_keyFilePath);
            }
        }
        catch
        {
            // 临时文件清理失败不影响测试结果。
        }
    }

    private static void SetEnvironmentVariable(string name, string? value)
    {
        if (value is null)
        {
            Environment.SetEnvironmentVariable(name, null);
        }
        else
        {
            Environment.SetEnvironmentVariable(name, value);
        }
    }

    [Fact]
    public void GetKey_FromEnvironmentVariable_ReturnsKey()
    {
        var expectedKey = new byte[32];
        Random.Shared.NextBytes(expectedKey);
        var hexKey = Convert.ToHexString(expectedKey);
        Environment.SetEnvironmentVariable("FLOWENGINE_CRYPTO_KEY", hexKey);

        var provider = new CryptoKeyProvider(_keyFilePath);
        var key = provider.GetKey();

        Assert.Equal(expectedKey, key);
    }

    [Fact]
    public void GetKey_FromFile_CreatesAndLoadsKey()
    {
        Environment.SetEnvironmentVariable("FLOWENGINE_CRYPTO_KEY", null);
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");

        var provider = new CryptoKeyProvider(_keyFilePath);
        var key = provider.GetKey();

        Assert.Equal(32, key.Length);
        Assert.True(File.Exists(_keyFilePath));
        var fileHex = File.ReadAllText(_keyFilePath).Trim();
        Assert.Equal(Convert.ToHexString(key), fileHex, ignoreCase: true);
    }

    [Fact]
    public void GetKey_FromExistingFile_LoadsSameKey()
    {
        Environment.SetEnvironmentVariable("FLOWENGINE_CRYPTO_KEY", null);
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        var existingKey = new byte[32];
        Random.Shared.NextBytes(existingKey);
        File.WriteAllText(_keyFilePath, Convert.ToHexString(existingKey));

        var provider = new CryptoKeyProvider(_keyFilePath);
        var key = provider.GetKey();

        Assert.Equal(existingKey, key);
    }

    [Fact]
    public void GetKey_ProductionWithoutEnv_ThrowsInvalidOperationException()
    {
        Environment.SetEnvironmentVariable("FLOWENGINE_CRYPTO_KEY", null);
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Production");

        var provider = new CryptoKeyProvider(_keyFilePath);

        Assert.Throws<InvalidOperationException>(() => provider.GetKey());
    }

    [Fact]
    public void GetKey_InvalidHexEnvironment_ThrowsInvalidOperationException()
    {
        Environment.SetEnvironmentVariable("FLOWENGINE_CRYPTO_KEY", "not-hex");

        var provider = new CryptoKeyProvider(_keyFilePath);

        Assert.Throws<InvalidOperationException>(() => provider.GetKey());
    }

    [Fact]
    public void GetKey_WrongLengthEnvironment_ThrowsInvalidOperationException()
    {
        Environment.SetEnvironmentVariable("FLOWENGINE_CRYPTO_KEY", "00112233");

        var provider = new CryptoKeyProvider(_keyFilePath);

        Assert.Throws<InvalidOperationException>(() => provider.GetKey());
    }

    [Fact]
    public void GetKey_ReturnsDefensiveCopy()
    {
        var expectedKey = new byte[32];
        Random.Shared.NextBytes(expectedKey);
        Environment.SetEnvironmentVariable("FLOWENGINE_CRYPTO_KEY", Convert.ToHexString(expectedKey));

        var provider = new CryptoKeyProvider(_keyFilePath);
        var key1 = provider.GetKey();
        var key2 = provider.GetKey();

        Assert.NotSame(key1, key2);
        Assert.Equal(key1, key2);
    }
}

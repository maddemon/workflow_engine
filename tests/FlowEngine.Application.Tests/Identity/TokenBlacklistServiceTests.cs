using FlowEngine.Application.Identity;
using FlowEngine.Infrastructure.Identity;
using Microsoft.Extensions.Caching.Memory;

namespace FlowEngine.Application.Tests.Identity;

/// <summary>
/// Token 黑名单服务测试。
/// </summary>
public class TokenBlacklistServiceTests : IDisposable
{
    private readonly MemoryCache _memoryCache;
    private readonly TokenBlacklistService _service;

    public TokenBlacklistServiceTests()
    {
        _memoryCache = new MemoryCache(new MemoryCacheOptions());
        _service = new TokenBlacklistService(_memoryCache);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _memoryCache.Dispose();
    }

    [Fact]
    public async Task IsBlacklistedAsync_UnknownJti_ReturnsFalse()
    {
        var result = await _service.IsBlacklistedAsync("unknown-jti", CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task AddAsync_AndIsBlacklistedAsync_ReturnsTrue()
    {
        var jti = "revoked-jti";
        var expiration = DateTime.UtcNow.AddMinutes(30);

        await _service.AddAsync(jti, expiration, CancellationToken.None);
        var result = await _service.IsBlacklistedAsync(jti, CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task IsBlacklistedAsync_ExpiredEntry_ReturnsFalse()
    {
        var jti = "expired-jti";
        var expiration = DateTime.UtcNow.AddMilliseconds(10);

        await _service.AddAsync(jti, expiration, CancellationToken.None);
        await Task.Delay(50, CancellationToken.None);
        var result = await _service.IsBlacklistedAsync(jti, CancellationToken.None);

        Assert.False(result);
    }
}

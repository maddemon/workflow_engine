using FlowEngine.Infrastructure.Identity;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace FlowEngine.Infrastructure.Tests.Identity;

public sealed class TokenBlacklistServiceTests : IDisposable
{
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private readonly TokenBlacklistService _service;

    public TokenBlacklistServiceTests()
    {
        _service = new TokenBlacklistService(_cache);
    }

    public void Dispose()
    {
        _cache.Dispose();
    }

    [Fact]
    public async Task IsBlacklistedAsync_UnknownJti_ReturnsFalse()
    {
        var result = await _service.IsBlacklistedAsync(Guid.NewGuid().ToString(), TestContext.Current.CancellationToken);

        Assert.False(result);
    }

    [Fact]
    public async Task AddAsync_AndIsBlacklistedAsync_ReturnsTrue()
    {
        var jti = Guid.NewGuid().ToString();
        var expiresAt = DateTime.UtcNow.AddHours(1);

        await _service.AddAsync(jti, expiresAt, TestContext.Current.CancellationToken);
        var result = await _service.IsBlacklistedAsync(jti, TestContext.Current.CancellationToken);

        Assert.True(result);
    }

    [Fact]
    public async Task AddAsync_ExpiredToken_UsesFallbackExpiration()
    {
        var jti = Guid.NewGuid().ToString();
        var expiresAt = DateTime.UtcNow.AddHours(-1);

        await _service.AddAsync(jti, expiresAt, TestContext.Current.CancellationToken);
        var result = await _service.IsBlacklistedAsync(jti, TestContext.Current.CancellationToken);

        Assert.True(result);
    }
}

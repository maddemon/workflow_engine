using FlowEngine.Infrastructure.Audit;
using Xunit;

namespace FlowEngine.Infrastructure.Tests.Audit;

public sealed class AuditNetBootstrapTests
{
    [Fact]
    public void EnsureConfigured_FirstCall_DoesNotThrow()
    {
        AuditNetBootstrap.EnsureConfigured();
    }

    [Fact]
    public void EnsureConfigured_SecondCall_IsIdempotent()
    {
        AuditNetBootstrap.EnsureConfigured();
        AuditNetBootstrap.EnsureConfigured();
    }
}

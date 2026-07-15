using FlowEngine.Core.Abstractions;

namespace FlowEngine.Application.Tests.TestSupport.Fakes;

public sealed class StubKeyProvider : ICryptoKeyProvider
{
    public byte[] GetKey() => new byte[32];
}

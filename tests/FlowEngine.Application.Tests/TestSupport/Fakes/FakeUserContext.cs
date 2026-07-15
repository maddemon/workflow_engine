using FlowEngine.Application.Identity;

namespace FlowEngine.Application.Tests.TestSupport.Fakes;

public sealed class FakeUserContext : IUserContext
{
    public bool IsAuthenticated => UserId.HasValue;

    public Guid? UserId { get; set; } = Guid.NewGuid();

    public string? Email { get; set; } = "test@test.com";

    public IReadOnlyList<string> Roles { get; set; } = [];
}

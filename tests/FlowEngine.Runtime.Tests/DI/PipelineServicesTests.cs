using System.Threading;
using System.Threading.Tasks;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Infrastructure.Services;
using Xunit;

namespace FlowEngine.Runtime.Tests.DI;

/// <summary>
/// Phase 3 独立 DI 服务（<see cref="IWorkflowMemoryService"/> / <see cref="IRecursionGuard"/> /
/// <see cref="ICredentialService"/>）的基本行为测试，直接构造实现验证语义。
/// </summary>
public sealed class PipelineServicesTests
{
    [Fact]
    public void WorkflowMemoryService_SetGetAndSnapshot_Work()
    {
        var mem = new WorkflowMemoryService();

        mem.Set("count", 42);
        mem.Set("name", "hello");

        Assert.Equal(42, mem.Get<int>("count"));
        Assert.Equal("hello", mem.Get<string>("name"));

        var snapshot = mem.Snapshot();
        Assert.Contains(snapshot, kv => kv.Key == "count");
        Assert.Contains(snapshot, kv => kv.Key == "name");
    }

    [Fact]
    public void WorkflowMemoryService_GetMissing_ReturnsDefault()
    {
        var mem = new WorkflowMemoryService();
        Assert.Equal(0, mem.Get<int>("absent"));
        Assert.Null(mem.Get<string>("absent"));
    }

    [Fact]
    public void RecursionGuard_RejectsBeyondMaxDepth_AndRecoversAfterExit()
    {
        var guard = new RecursionGuard(3);

        Assert.True(guard.TryEnter("n"));
        Assert.True(guard.TryEnter("n"));
        Assert.True(guard.TryEnter("n"));
        // 第 4 次超过上限。
        Assert.False(guard.TryEnter("n"));

        guard.Exit("n");
        // 退出后深度回到 3，可再次进入。
        Assert.True(guard.TryEnter("n"));

        // 不同节点互不影响。
        Assert.True(guard.TryEnter("other"));
    }

    [Fact]
    public async Task CredentialService_ResolvesByNameAndId_AndReturnsNullWhenMissing()
    {
        var svc = new NodeCredentialService(new FakeAccessor());

        var byName = await svc.ResolveAsync("byName", CancellationToken.None);
        Assert.NotNull(byName);
        Assert.Equal("byName", byName!.Name);

        var id = Guid.NewGuid();
        var byId = await svc.ResolveAsync(id.ToString(), CancellationToken.None);
        Assert.NotNull(byId);
        Assert.Equal(id, byId!.Id);

        Assert.Null(await svc.ResolveAsync("nope", CancellationToken.None));
        Assert.Null(await svc.ResolveAsync(null, CancellationToken.None));
    }

    private sealed class FakeAccessor : ICredentialAccessor
    {
        public Task<CredentialValue> GetCredentialAsync(Guid credentialId, CancellationToken cancellationToken = default)
            => Task.FromResult(new CredentialValue { Id = credentialId, Name = credentialId.ToString() });

        public Task<CredentialValue?> GetCredentialByNameAsync(string name, CancellationToken cancellationToken = default)
            => Task.FromResult<CredentialValue?>(name == "byName" ? new CredentialValue { Name = "byName" } : null);
    }
}

using FlowEngine.Core.Entities;
using FlowEngine.Runtime.Executor;
using Xunit;

namespace FlowEngine.Runtime.Tests.Executor;

/// <summary>
/// <see cref="ExecutionQueue"/> 与 <see cref="NodeWorkItem"/> 的入队/出队覆盖测试。
/// </summary>
public class ExecutionQueueTests
{
    [Fact]
    public async Task Enqueue_ThenDequeue_ReturnsSameItem()
    {
        var queue = new ExecutionQueue();
        var item = new NodeWorkItem(Guid.NewGuid(), "node1", new Dictionary<string, DataBatch>());

        await queue.EnqueueAsync(item);
        var dequeued = await queue.DequeueAsync();

        Assert.Same(item, dequeued);
        Assert.NotNull(queue.Reader);
    }

    [Fact]
    public void NodeWorkItem_RecordProperties_Assignable()
    {
        var id = Guid.NewGuid();
        var item = new NodeWorkItem(id, "n", new Dictionary<string, DataBatch>());
        Assert.Equal(id, item.ExecutionId);
        Assert.Equal("n", item.NodeInstanceId);
        Assert.NotNull(item.Inputs);
    }
}

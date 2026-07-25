using System.Linq;
using System.Reflection;
using System.Threading;
using System.Collections.Generic;
using FlowEngine.Core.Abstractions;
using FlowEngine.Host.Tests.Infrastructure;
using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Host.Executor;
using FlowEngine.Host.Tests;
using FlowEngine.Runtime.Executor;
using FlowEngine.Runtime.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FlowEngine.Host.Tests.Executor;

/// <summary>
/// P3 #20：验证 WorkflowExecutionWorker 为每个执行项在独立 scope 内解析 WorkflowExecutor
/// 与其 scoped DbContext，避免长生命周期 scope 捕获 DbContext 导致跨执行数据污染。
/// </summary>
public sealed class WorkflowExecutionWorkerScopeTests : HostIntegrationTestBase
{
    public WorkflowExecutionWorkerScopeTests(FlowEngineWebApplicationFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task Execute_SequentialItems_ResolveIndependentScopedDbContexts()
    {
        var ct = TestContext.Current.CancellationToken;

        // 从真实宿主获取执行引擎的重依赖（仅用于构造 WorkflowExecutor；运行路径不触达内核）。
        // 这些依赖为 Scoped 服务，必须从创建的 scope 中解析，不能从根 IServiceProvider 解析。
        using var resolveScope = _factory.Services.CreateScope();
        var rsp = resolveScope.ServiceProvider;
        var nodeRegistry = rsp.GetRequiredService<INodeRegistry>();
        var contextFactory = rsp.GetRequiredService<NodeExecutionContextFactory>();
        var errorHandler = rsp.GetRequiredService<ErrorStrategyHandler>();
        var secretMasker = rsp.GetRequiredService<SecretMasker>();
        var execLogger = rsp.GetRequiredService<ILogger<WorkflowExecutor>>();
        var kernelLogger = rsp.GetRequiredService<ILogger<WorkflowSchedulerKernel>>();

        var queue = new WorkflowExecutionQueue();
        var cancellationRegistry = new ExecutionCancellationRegistry();

        // 记录每个执行项在各自的 execution scope 中解析到的 DbContext 实例。
        var resolvedDbContexts = new List<FlowEngineDbContext>();

        var collection = new ServiceCollection();
        collection.AddSingleton(nodeRegistry);
        collection.AddSingleton(contextFactory);
        collection.AddSingleton(errorHandler);
        collection.AddSingleton(secretMasker);
        collection.AddSingleton(execLogger);
        collection.AddSingleton(kernelLogger);
        collection.AddSingleton(queue);
        collection.AddSingleton(cancellationRegistry);
        collection.AddDbContext<FlowEngineDbContext>(o => o.UseInMemoryDatabase("worker-scope-test"));
        collection.AddScoped<WorkflowExecutor>(sp =>
        {
            var db = sp.GetRequiredService<FlowEngineDbContext>();
            resolvedDbContexts.Add(db);
            return new WorkflowExecutor(db, nodeRegistry, contextFactory, errorHandler, queue, execLogger, kernelLogger, secretMasker);
        });

        var provider = collection.BuildServiceProvider();
        IServiceScopeFactory scopeFactory = new DelegatingScopeFactory(provider);

        // 预置工作流与两个已终态的执行记录（Status=Completed 使 ExecuteLoopAsync 提前返回，避免触达内核）。
        Guid workflowId;
        var recordIds = new List<Guid>();
        using (var seedScope = scopeFactory.CreateScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<FlowEngineDbContext>();
            var workflow = new Workflow
            {
                Name = "W",
                ProjectId = Guid.NewGuid(),
                Nodes = [],
                Connections = [],
                CreatedBy = "test",
                Version = 1,
                IsActive = true,
            };
            db.Workflows.Add(workflow);
            await db.SaveChangesAsync(ct);
            workflowId = workflow.Id;

            foreach (var _ in Enumerable.Range(0, 2))
            {
                var record = new ExecutionRecord
                {
                    WorkflowDefinitionId = workflowId,
                    ProjectId = workflow.ProjectId,
                    Status = ExecutionStatus.Completed,
                    StartedAt = DateTime.UtcNow,
                    CompletedAt = DateTime.UtcNow,
                    NodeRecords = [],
                };
                db.ExecutionRecords.Add(record);
                recordIds.Add(record.Id);
            }

            await db.SaveChangesAsync(ct);

            await queue.EnqueueAsync(new WorkflowExecutionWorkItem(recordIds[0], workflowId, null), ct);
            await queue.EnqueueAsync(new WorkflowExecutionWorkItem(recordIds[1], workflowId, null), ct);
        }

        // 注意：构造时 lifetime 参数在当前实现中未被 ExecuteAsync 使用，传 null 即可。
        var worker = new WorkflowExecutionWorker(scopeFactory, null!, NullLogger<WorkflowExecutionWorker>.Instance);

        using var stoppingCts = new CancellationTokenSource();
        var executeMethod = typeof(BackgroundService).GetMethod("ExecuteAsync", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("未能通过反射获取 ExecuteAsync。");
        var workerTask = (Task)executeMethod.Invoke(worker, new object[] { stoppingCts.Token })!;

        // 等待两个执行项均被处理（各自解析出独立 DbContext）。
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (resolvedDbContexts.Count < 2 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20, ct);
        }

        // 每个执行项应在独立 scope 中解析出各自的 DbContext，且彼此不是同一实例（无跨执行共享）。
        Assert.Equal(2, resolvedDbContexts.Count);
        Assert.NotSame(resolvedDbContexts[0], resolvedDbContexts[1]);

        // 通知 worker 退出后台循环。
        stoppingCts.Cancel();
        await Task.WhenAny(workerTask, Task.Delay(5000, ct));
        Assert.True(workerTask.IsCompleted);
    }

    /// <summary>
    /// CON-2：验证工作流执行后台服务并发消费队列——多个执行项并行处理且互不阻塞。
    /// 探针节点在 ExecuteAsync 中记录当前活跃执行数（静态计数），并 await 100ms 制造重叠窗口；
    /// 若 worker 仍串行处理，MaxActive 应为 1，并需累计约 N×100ms；并发时 MaxActive≥2。
    /// </summary>
    [Fact]
    public async Task Execute_ConcurrentItems_RunInParallel()
    {
        var ct = TestContext.Current.CancellationToken;

        using var resolveScope = _factory.Services.CreateScope();
        var rsp = resolveScope.ServiceProvider;
        var nodeRegistry = rsp.GetRequiredService<INodeRegistry>();
        nodeRegistry.Register(new ConcurrencyProbeNode());
        var contextFactory = rsp.GetRequiredService<NodeExecutionContextFactory>();
        var errorHandler = rsp.GetRequiredService<ErrorStrategyHandler>();
        var secretMasker = rsp.GetRequiredService<SecretMasker>();
        var execLogger = rsp.GetRequiredService<ILogger<WorkflowExecutor>>();
        var kernelLogger = rsp.GetRequiredService<ILogger<WorkflowSchedulerKernel>>();

        var queue = new WorkflowExecutionQueue();
        var cancellationRegistry = new ExecutionCancellationRegistry();

        var resolvedDbContexts = new List<FlowEngineDbContext>();

        var collection = new ServiceCollection();
        collection.AddSingleton(nodeRegistry);
        collection.AddSingleton(contextFactory);
        collection.AddSingleton(errorHandler);
        collection.AddSingleton(secretMasker);
        collection.AddSingleton(execLogger);
        collection.AddSingleton(kernelLogger);
        collection.AddSingleton(queue);
        collection.AddSingleton(cancellationRegistry);
        collection.AddDbContext<FlowEngineDbContext>(o => o.UseInMemoryDatabase("worker-concurrency-test"));
        collection.AddScoped<WorkflowExecutor>(sp =>
        {
            var db = sp.GetRequiredService<FlowEngineDbContext>();
            resolvedDbContexts.Add(db);
            return new WorkflowExecutor(db, nodeRegistry, contextFactory, errorHandler, queue, execLogger, kernelLogger, secretMasker);
        });

        var provider = collection.BuildServiceProvider();
        IServiceScopeFactory scopeFactory = new DelegatingScopeFactory(provider);

        var workflow = new Workflow
        {
            Name = "W",
            ProjectId = Guid.NewGuid(),
            Nodes =
            [
                new NodeDefinition
                {
                    Id = "n1",
                    Name = "n1",
                    TypeName = "concurrencyProbe",
                    IsEntry = true,
                    Parameters = [],
                    ErrorStrategy = ErrorStrategy.Terminate,
                },
            ],
            Connections = [],
            CreatedBy = "test",
            Version = 1,
            IsActive = true,
        };
        var workflowId = workflow.Id;

        const int itemCount = 4;
        using (var seedScope = scopeFactory.CreateScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<FlowEngineDbContext>();
            foreach (var _ in Enumerable.Range(0, itemCount))
            {
                var record = new ExecutionRecord
                {
                    WorkflowDefinitionId = workflowId,
                    ProjectId = workflow.ProjectId,
                    Status = ExecutionStatus.Pending,
                    StartedAt = DateTime.UtcNow,
                    NodeRecords = [],
                };
                db.ExecutionRecords.Add(record);
                await db.SaveChangesAsync(ct);
                await queue.EnqueueAsync(new WorkflowExecutionWorkItem(record.Id, workflowId, null, workflow), ct);
            }
        }

        ConcurrencyProbeNode.Reset();
        var worker = new WorkflowExecutionWorker(scopeFactory, null!, NullLogger<WorkflowExecutionWorker>.Instance);

        using var stoppingCts = new CancellationTokenSource();
        var executeMethod = typeof(BackgroundService).GetMethod("ExecuteAsync", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("未能通过反射获取 ExecuteAsync。");
        var workerTask = (Task)executeMethod.Invoke(worker, new object[] { stoppingCts.Token })!;

        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (ConcurrencyProbeNode.Completed < itemCount && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20, ct);
        }

        // 全部执行完成，且执行过程存在并行（MaxActive≥2），证明 worker 并发消费队列（CON-2）。
        Assert.Equal(itemCount, ConcurrencyProbeNode.Completed);
        Assert.True(
            ConcurrencyProbeNode.MaxActive >= 2,
            $"期望并行执行（MaxActive>=2），实际 {ConcurrencyProbeNode.MaxActive}。");

        stoppingCts.Cancel();
        await Task.WhenAny(workerTask, Task.Delay(5000, ct));
        Assert.True(workerTask.IsCompleted);
    }

    /// <summary>
    /// P3 #20 衍生：验证工作项携带 <see cref="WorkflowExecutionWorkItem.PreloadedWorkflow"/> 时，
    /// worker 直接复用该实例而无需重新查询 Workflows。本用例刻意不将工作流写入数据库，
    /// 若 worker 回退到数据库查询会得到 null 并跳过执行，从而可断言透传生效。
    /// </summary>
    [Fact]
    public async Task Execute_WithPreloadedWorkflow_ReusesInstanceWithoutRequery()
    {
        var ct = TestContext.Current.CancellationToken;

        using var resolveScope = _factory.Services.CreateScope();
        var rsp = resolveScope.ServiceProvider;
        var nodeRegistry = rsp.GetRequiredService<INodeRegistry>();
        var contextFactory = rsp.GetRequiredService<NodeExecutionContextFactory>();
        var errorHandler = rsp.GetRequiredService<ErrorStrategyHandler>();
        var secretMasker = rsp.GetRequiredService<SecretMasker>();
        var execLogger = rsp.GetRequiredService<ILogger<WorkflowExecutor>>();
        var kernelLogger = rsp.GetRequiredService<ILogger<WorkflowSchedulerKernel>>();

        var queue = new WorkflowExecutionQueue();
        var cancellationRegistry = new ExecutionCancellationRegistry();

        // 记录每个执行项在各自 execution scope 中解析到的 DbContext 实例。
        var resolvedDbContexts = new List<FlowEngineDbContext>();

        var collection = new ServiceCollection();
        collection.AddSingleton(nodeRegistry);
        collection.AddSingleton(contextFactory);
        collection.AddSingleton(errorHandler);
        collection.AddSingleton(secretMasker);
        collection.AddSingleton(execLogger);
        collection.AddSingleton(kernelLogger);
        collection.AddSingleton(queue);
        collection.AddSingleton(cancellationRegistry);
        collection.AddDbContext<FlowEngineDbContext>(o => o.UseInMemoryDatabase("worker-preloaded-test"));
        collection.AddScoped<WorkflowExecutor>(sp =>
        {
            var db = sp.GetRequiredService<FlowEngineDbContext>();
            resolvedDbContexts.Add(db);
            return new WorkflowExecutor(db, nodeRegistry, contextFactory, errorHandler, queue, execLogger, kernelLogger, secretMasker);
        });

        var provider = collection.BuildServiceProvider();
        IServiceScopeFactory scopeFactory = new DelegatingScopeFactory(provider);

        // 仅写入执行记录（Completed 使 ExecuteLoopAsync 提前返回，不触达内核），
        // 但刻意不将工作流写入数据库，以验证 worker 完全依赖 PreloadedWorkflow。
        Guid workflowId;
        var recordIds = new List<Guid>();
        using (var seedScope = scopeFactory.CreateScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<FlowEngineDbContext>();
            var workflow = new Workflow
            {
                Name = "W",
                ProjectId = Guid.NewGuid(),
                Nodes = [],
                Connections = [],
                CreatedBy = "test",
                Version = 1,
                IsActive = true,
            };
            workflowId = workflow.Id;

            var record = new ExecutionRecord
            {
                WorkflowDefinitionId = workflowId,
                ProjectId = workflow.ProjectId,
                Status = ExecutionStatus.Completed,
                StartedAt = DateTime.UtcNow,
                CompletedAt = DateTime.UtcNow,
                NodeRecords = [],
            };
            db.ExecutionRecords.Add(record);
            recordIds.Add(record.Id);

            await db.SaveChangesAsync(ct);

            // 工作项随带 PreloadedWorkflow；若该实例未被使用，worker 会回退查询并得到 null 而跳过。
            await queue.EnqueueAsync(new WorkflowExecutionWorkItem(recordIds[0], workflowId, null, workflow), ct);
        }

        var worker = new WorkflowExecutionWorker(scopeFactory, null!, NullLogger<WorkflowExecutionWorker>.Instance);

        using var stoppingCts = new CancellationTokenSource();
        var executeMethod = typeof(BackgroundService).GetMethod("ExecuteAsync", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("未能通过反射获取 ExecuteAsync。");
        var workerTask = (Task)executeMethod.Invoke(worker, new object[] { stoppingCts.Token })!;

        // 等待执行项被处理（依赖预加载实例，未查询 Workflows）。
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (resolvedDbContexts.Count < 1 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20, ct);
        }

        // worker 使用了预加载工作流（否则因 Workflows 为空会跳过，resolvedDbContexts 仍为 0）。
        Assert.Single(resolvedDbContexts);

        stoppingCts.Cancel();
        await Task.WhenAny(workerTask, Task.Delay(5000, ct));
        Assert.True(workerTask.IsCompleted);
    }

    /// <summary>
    /// 将 <see cref="ServiceProvider"/> 适配为 <see cref="IServiceScopeFactory"/>。
    /// <see cref="ServiceProvider"/> 显式实现该接口，直接赋值转换在编译期不可见，故在此委托转发。
    /// </summary>
    private sealed class DelegatingScopeFactory(ServiceProvider inner) : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => inner.CreateScope();
    }

    /// <summary>
    /// 探针节点（CON-2）：在执行期间记录当前活跃执行数（静态计数），并 await 制造重叠窗口，
    /// 用于验证 worker 并发消费队列时多个执行确实并行运行。
    /// </summary>
    private sealed class ConcurrencyProbeNode : INodeType
    {
        public static int Active;
        public static int MaxActive;
        public static int Completed;

        public static void Reset()
        {
            Active = 0;
            MaxActive = 0;
            Completed = 0;
        }

        public string TypeName => "concurrencyProbe";
        public string DisplayName => "Probe";
        public string Category => "Test";
        public string Icon => string.Empty;
        public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;
        public IReadOnlyList<PortDefinition> Ports => new[]
        {
            new PortDefinition { Name = "input", Direction = PortDirection.Input, Type = PortType.Main },
        };
        public bool DefaultIsEntry => true;

        public async Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default)
        {
            var active = Interlocked.Increment(ref Active);
            var max = MaxActive;
            while (active > max)
            {
                max = Interlocked.CompareExchange(ref MaxActive, active, max);
            }

            try
            {
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                return new NodeExecutionResult { Success = true, Output = new DataBatch() };
            }
            finally
            {
                Interlocked.Decrement(ref Active);
                Interlocked.Increment(ref Completed);
            }
        }
    }
}

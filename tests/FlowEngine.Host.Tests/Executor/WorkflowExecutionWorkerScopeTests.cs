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
            // 工作流须持久化到执行作用域数据库：worker 依据 Id 在执行作用域内重新加载，不再随工作项携带实体。
            db.Workflows.Add(workflow);
            await db.SaveChangesAsync(ct);
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
                await queue.EnqueueAsync(new WorkflowExecutionWorkItem(record.Id, workflowId, null), ct);
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
    /// Task 7 验证：工作项仅携带工作流定义 ID，worker 在各自执行作用域内依据 ID 重新加载工作流定义，
    /// 不再依赖随工作项跨作用域携带的实体。本用例将工作流与执行记录写入 worker 执行作用域的数据库，
    /// 仅以 Id 入队；若 worker 能正确重新加载并执行（执行记录落库为 Completed），
    /// 证明跨 DbContext 作用域的实体复用已被移除。
    /// </summary>
    [Fact]
    public async Task Execute_WithoutPreloadedWorkflow_ReloadsFromExecutionScopeAndRuns()
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
        collection.AddDbContext<FlowEngineDbContext>(o => o.UseInMemoryDatabase("worker-reload-test"));
        collection.AddScoped<WorkflowExecutor>(sp =>
        {
            var db = sp.GetRequiredService<FlowEngineDbContext>();
            resolvedDbContexts.Add(db);
            return new WorkflowExecutor(db, nodeRegistry, contextFactory, errorHandler, queue, execLogger, kernelLogger, secretMasker);
        });

        var provider = collection.BuildServiceProvider();
        IServiceScopeFactory scopeFactory = new DelegatingScopeFactory(provider);

        // 将工作流与执行记录写入 worker 执行作用域的数据库；工作项仅携带 Id。
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
                Status = ExecutionStatus.Pending,
                StartedAt = DateTime.UtcNow,
                NodeRecords = [],
            };
            db.Workflows.Add(workflow);
            db.ExecutionRecords.Add(record);
            recordIds.Add(record.Id);

            await db.SaveChangesAsync(ct);

            // 仅以 Id 入队（不携带任何实体引用）。
            await queue.EnqueueAsync(new WorkflowExecutionWorkItem(recordIds[0], workflowId, null), ct);
        }

        var worker = new WorkflowExecutionWorker(scopeFactory, null!, NullLogger<WorkflowExecutionWorker>.Instance);

        using var stoppingCts = new CancellationTokenSource();
        var executeMethod = typeof(BackgroundService).GetMethod("ExecuteAsync", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("未能通过反射获取 ExecuteAsync。");
        var workerTask = (Task)executeMethod.Invoke(worker, new object[] { stoppingCts.Token })!;

        // 等待执行项被处理（worker 重新加载工作流并执行）。
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (resolvedDbContexts.Count < 1 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20, ct);
        }

        // worker 在独立执行作用域内解析出 DbContext 并依据 Id 重新加载工作流执行。
        Assert.Single(resolvedDbContexts);

        // 执行已真正运行：执行记录由 Pending 落库为 Completed，证明 worker 在作用域内重新加载并运行。
        using (var verifyScope = scopeFactory.CreateScope())
        {
            var db = verifyScope.ServiceProvider.GetRequiredService<FlowEngineDbContext>();
            var record = await db.ExecutionRecords
                .FirstOrDefaultAsync(e => e.Id == recordIds[0], ct);
            Assert.NotNull(record);
            Assert.Equal(ExecutionStatus.Completed, record.Status);
        }

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

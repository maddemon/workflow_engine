# 部署架构

## 1. 默认部署形态：单机后台服务

Flow Engine 的默认部署形态是**单个 .NET 后台服务进程**，同时承载以下能力：

- HTTP API（ASP.NET Core / Kestrel）
- 前端静态文件托管（`wwwroot/`）
- 执行引擎主循环
- 触发器调度（Quartz.NET）
- Webhook 路由注册与请求处理
- 节点插件扫描与注册

```
┌─────────────────────────────────────────┐
│         Flow Engine 后台服务进程          │
│  ┌─────────────┐  ┌───────────────────┐ │
│  │  Kestrel    │  │  执行引擎          │ │
│  │  HTTP API   │  │  内存执行队列       │ │
│  │  wwwroot/   │  │  多输入等待/错误处理 │ │
│  └──────┬──────┘  └───────────────────┘ │
│         │                               │
│  ┌──────┴───────────────────────────┐  │
│  │        Quartz.NET 调度器          │  │
│  │   Schedule 触发器 / 轮询触发器     │  │
│  └───────────────────────────────────┘  │
│         │                               │
│  ┌──────┴───────────────────────────┐  │
│  │        Webhook 路由表             │  │
│  └───────────────────────────────────┘  │
│         │                               │
│  ┌──────┴───────────────────────────┐  │
│  │        节点注册中心               │  │
│  │     扫描 plugins/*.dll           │  │
│  └───────────────────────────────────┘  │
└─────────────────────────────────────────┘
            │
    ┌───────┴───────┐
    ▼               ▼
 SQLite 文件    本地文件存储
 plugins/       storage/
 logs/          wwwroot/
```

## 2. 前端集成到后端

前端使用 React/TypeScript 开发，构建后输出到后端项目的 `wwwroot/` 目录：

```
frontend/
  npm run build
    ↓
  dist/ → 复制到 backend/wwwroot/
    ↓
  后端 UseStaticFiles + MapFallbackToFile("index.html")
```

这样对外只需要启动一个后台服务，浏览器访问 `http://localhost:5000/` 即可加载前端。

## 3. 默认技术栈

| 组件 | 默认选型 | 说明 |
|------|---------|------|
| 数据库 | SQLite | 单文件，零配置 |
| 任务调度 | Quartz.NET | 内存 JobStore，默认单机 |
| 事件总线 | 内存实现 | 自定义 `IEventBus`，单机订阅 |
| 执行队列 | 内存 `Channel<T>` | 无需 Redis；执行入队前先持久化 ExecutionRecord 为 Pending |
| 文件存储 | 本地文件系统 | `storage/` 目录 |
| 审计日志 | 本地 NDJSON 文件 | `logs/audit/` |
| 节点插件 | 本地 DLL | `plugins/` 目录 |

## 4. 运行方式

### 4.1 开发环境

```bash
# 后端
cd backend
dotnet run

# 前端（开发时反向代理到后端 API）
cd frontend
npm run dev
```

开发时前端使用 Vite dev server，通过 `vite.config.ts` 代理 API 请求到后端。

### 4.2 Windows 生产环境

发布为 Windows Service：

```powershell
# 发布
dotnet publish -c Release -o ./publish

# 注册为 Windows Service（使用 .NET 的 --service 参数或 sc.exe）
sc.exe create FlowEngine binPath= "C:\app\FlowEngine.exe --service"
sc.exe start FlowEngine
```

### 4.3 Linux 生产环境

发布并配置 systemd：

```bash
# 发布
dotnet publish -c Release -r linux-x64 --self-contained false -o ./publish

# systemd service 文件 /etc/systemd/system/flowengine.service
sudo systemctl enable flowengine
sudo systemctl start flowengine
```

### 4.4 Docker

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY ./publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "FlowEngine.dll"]
```

## 5. 横向扩展预留

虽然默认单机运行，但架构设计保留横向扩展能力：

| 组件 | 单机状态 | 横向扩展时替换为 |
|------|---------|----------------|
| 数据库 | SQLite | PostgreSQL / MySQL / SQL Server |
| 任务调度 | Quartz 内存 JobStore | Quartz ADO.NET JobStore + 多实例竞争 |
| 执行队列 | 内存 `Channel<T>` | Redis + 独立 Worker 进程 |
| 事件总线 | 内存实现 | RabbitMQ / Kafka |
| 文件存储 | 本地文件系统 | S3 / MinIO / NAS |
| 审计日志 | 本地文件 | ELK / 外部日志系统 |

**状态外置原则**：

- 执行状态、触发器状态、凭据、工作流定义全部持久化到数据库。
- 节点插件通过共享存储或镜像分发。
- 执行引擎实例尽量无状态，方便多实例部署。

## 6. 何时需要横向扩展

| 场景 | 建议 |
|------|------|
| 日执行次数 < 1 万 | 单机 SQLite 足够 |
| 日执行次数 1~10 万 | 切换 PostgreSQL + Quartz ADO.NET JobStore |
| 日执行次数 > 10 万 | 引入 Redis 队列 + 独立 Worker |
| 需要高可用 | 多实例 + 负载均衡 + 共享数据库 |
| 大量 Webhook 并发 | 多实例 + 共享路由注册表 |
| 多用户实时协作编辑 | Yjs WebSocket 服务器多节点 + 房间路由 |

## 7. 企业扩展层分类

企业扩展能力按单机可承载性分类：

### 7.1 单机可承载

| 能力 | 说明 |
|------|------|
| RBAC 权限 | 纯逻辑层，单数据库即可 |
| 多租户/项目 | `projectId` 过滤，单数据库即可 |
| SSO / LDAP | 对接外部 IdP，本系统单机 |
| 文件存储 | 本地或 MinIO 单实例 |
| MCP 协议 | 协议层，单机无压力 |
| Git 版本管理 | 调用 Git 命令或 LibGit2Sharp |
| 外部凭据 Vault | 对接 HashiCorp Vault 等外部服务 |
| AI Builder | 调用 LLM API，单机无压力 |
| 审计合规导出 | 本地文件 + 数据库 |
| 监控指标 / OpenTelemetry | 本机采集，外部存储 |

### 7.2 需多机或外部服务

| 能力 | 说明 |
|------|------|
| Redis 队列 | 明确需要 Redis 实例 |
| 独立 Worker | 多进程/多机执行 |
| 大规模协作编辑 | WebSocket 长连接多节点 + 房间路由 |
| 高可用负载均衡 | 多实例部署 |
| 企业级文件存储 | S3 / 对象存储集群 |
| 外部日志系统 | ELK / Splunk / Datadog 集群 |

## 8. 配置文件示例

```json
{
  "ConnectionStrings": {
    "Default": "Data Source=flowengine.db"
  },
  "Quartz": {
    "JobStore": {
      "Type": "Quartz.Simpl.RAMJobStore, Quartz"
    }
  },
  "EventBus": {
    "Provider": "InMemory"
  },
  "Storage": {
    "Type": "LocalFileSystem",
    "Path": "./storage"
  },
  "Plugins": {
    "Path": "./plugins"
  }
}
```

## 9. 执行恢复与优雅关闭

### 9.1 执行恢复

执行入队前，引擎先将 `ExecutionRecord` 持久化为 `Pending` 状态。进程崩溃重启后，引擎扫描数据库中状态为 `Pending` 或 `Running` 的执行记录，重新入队恢复：

```csharp
public async Task RecoverPendingExecutionsAsync()
{
    var pending = await executionStore.GetByStatusAsync(
        ExecutionStatus.Pending,
        ExecutionStatus.Running);

    foreach (var record in pending)
    {
        await executionQueue.EnqueueAsync(record);
    }
}
```

**注意**：正在执行中的节点可能已在崩溃前产生副作用，恢复后节点会重新执行。为帮助节点实现幂等，`NodeExecutionContext` 提供 `LastExecutionRecord`：

```csharp
public class NodeExecutionContext
{
    /// <summary>
    /// 上次执行记录。崩溃恢复时提供，首次执行为 null。
    /// 节点可据此判断副作用是否已发生，避免重复执行。
    /// </summary>
    public NodeExecutionRecord LastExecutionRecord { get; set; }
}
```

节点实现应尽可能幂等；无法幂等时，通过 `LastExecutionRecord` 中的输出、外部标识（如订单号）或时间戳判断。

### 9.2 优雅关闭

服务停止时，通过 `IHostApplicationLifetime.ApplicationStopping` 钩子实现优雅关闭：

1. 停止接收新的执行请求和 Webhook。
2. 停止 Quartz 调度器触发新 Job。
3. 等待执行队列中已取出的节点执行完成（受 `ShutdownTimeout` 限制，默认 30 秒）。
4. 等待审计日志事件总线刷盘完成。
5. 取消未完成的执行并记录为 `Cancelled`。
6. 释放资源，退出进程。

```csharp
public class GracefulShutdownHostedService : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _lifetime.ApplicationStopping.Register(() =>
        {
            _ = StopInternalAsync(); // 1. 停止接收新请求
        });                           // 2. 停止 Quartz 调度器
        return Task.CompletedTask;    // 3. 等待当前执行完成（受 ShutdownTimeout 限制，默认 30 秒）
    }                                 // 4. 等待审计日志刷盘
                                      // 5. 取消未完成的执行并记录为 Cancelled
    private async Task StopInternalAsync()
    {
        _executionQueue.StopAcceptingNew();
        await _scheduler.Shutdown();
        await _executionQueue.WaitForCurrentToCompleteAsync(TimeSpan.FromSeconds(30));
        await _eventBus.FlushAndStopAsync();
    }

    public async Task StopAsync(CancellationToken cancellationToken) { ... }
}
```

## 10. 安全与权限

### 10.1 运行安全

- 后台服务以低权限用户运行。
- `plugins/`、`storage/`、`logs/` 目录权限最小化。
- SQLite 文件做好备份策略。
- 生产环境建议切换 PostgreSQL 并启用连接加密。

### 10.2 SQLite 高并发

默认 SQLite 连接字符串启用 **WAL（Write-Ahead Logging）** 模式，提升并发读写性能：

```json
{
  "ConnectionStrings": {
    "Default": "Data Source=flowengine.db;Mode=ReadWriteCreate;Cache=Shared;Journal Mode=WAL"
  }
}
```

WAL 模式下，读操作不会被写操作阻塞，适合中等并发场景。极高并发仍需切换 PostgreSQL。

### 10.3 健康检查与 API 基础

MVP 应包含以下基础端点：

- `GET /health`：服务健康状态，返回 200/503。
- `GET /health/ready`： readiness 探针，检查数据库、调度器等依赖是否就绪。
- API 统一前缀 `/api/v1/`，便于未来版本升级。
- `CORS`：默认仅允许前端域名访问，生产环境通过配置严格控制。
- `CSRF`：前后端同域部署时风险较低；若未来拆分部署，启用 anti-forgery token 或 SameSite cookie。

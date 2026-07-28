# 横向扩展路径（Scaling Path）

> 本文档基于当前代码编写，以代码为准。凡标注「待确认」者为代码未见明确实现，需后续核实或规划。

## 1. 总体判断

Flow Engine **当前设计为单机形态**：执行引擎、Quartz 调度、Webhook 路由、MCP 端点都在同一个 .NET 进程内。

但底层已为**多机就绪（multi-machine-ready）**预留能力：数据库提供器可切换为共享 PostgreSQL，文件存储与日志均为可替换抽象，认证为无状态 JWT。以下逐条列出「已支持」与「待规划」，不臆测未实现的部分。

## 2. 已支持的多实例前提

| 能力 | 现状（代码依据） | 多实例含义 |
|------|------------------|-----------|
| 共享数据库 | `ServiceCollectionExtensions.cs` 接入 **SQLite + PostgreSQL(Npgsql) + MySQL + KingbaseES + Dameng** EF 提供器（`UseSqlite` / `UseNpgsql` / `UseMySQL` / 达梦） | 将所有实例指向同一 PostgreSQL，即可共享工作流 / 凭据 / 执行记录 |
| DB 节点跨库直连 | 插件节点可经原始 ADO 驱动直连 **MySQL / SQL Server** | 与平台库解耦，数据源无关 |
| 无状态认证 | JWT 令牌（`Jwt` 配置）签发，API Key（Bearer）用于 MCP | 多实例前置负载均衡无需会话粘滞（Cookie 除外，见 §4） |
| 文件存储抽象 | `Storage:Type`（`LocalFileSystem`）可扩展为对象存储 | 多实例共享对象存储即可统一文件 |
| 审计 / 日志抽象 | `AuditLogFileSink` 落盘（NDJSON）；`AuditEventNotificationHandler` 经 `IEventBus` 通知；Serilog + OpenTelemetry（stdout 导出） | 可接外部日志/追踪后端（外部转发取决于 `IEventBus` 实现） |
| 凭据加密 | AES-GCM，密钥来自配置 | 多实例共用同一主密钥即可解密同一凭据 |

> 切换数据库提供器仅需改 `appsettings.json` 的 `Database.Provider` 与 `ConnectionStrings.Default`；迁移按提供器分离为 `FlowEngine.Migrations.Sqlite`（默认）与 `FlowEngine.Migrations.Postgres`（PostgreSQL / 金仓），运行期由 `FlowEngine.Host.MigrationsExtensions` 自动选用对应程序集应用迁移。

## 3. 尚未实现（规划 / 待确认）

| 议题 | 现状 | 多实例风险 |
|------|------|-----------|
| 执行引擎与 Worker 分离 | 执行在 Host 进程内，无独立 worker / 消息队列（grep 未发现 Redis / Channel / 分布式队列） | 多实例下同一工作流可能被多个实例重复触发 |
| Quartz 集群模式 | 当前未启用 Quartz 集群（仅 `services.AddQuartz()`，代码中无 `IsClustered` 配置） | 多实例各自调度 Schedule / Poll 触发器会导致重复执行 |
| 分布式锁 / 幂等 | 未见 Redis / 分布式锁 | 并发执行、清理任务可能冲突 |
| 文件存储共享 | 默认本地文件系统 `./storage/files` | 多实例若各自本地盘，文件互不可见 |
| WebSocket 投递 | `/ws/execution` 为有状态连接，绑定到单个实例 | 多实例下前端需 sticky session；否则执行事件推送不到正确实例 |
| 执行断点续跑 | 进程退出中止进行中执行（代码无断点续跑 / 恢复实现） | 实例重启丢失在途执行 |

## 4. 多实例部署建议（起步路径）

1. **共享数据库**：`Database.Provider = postgresql`，所有实例指向同一库。
2. **共享存储**：将 `Storage` 实现切换为共享对象存储（S3 等），或挂载共享卷。
3. **一致密钥**：所有实例 `Jwt:Secret` 与凭据主密钥保持一致。
4. **外部日志/追踪**：将 Serilog / OpenTelemetry 导出到集中后端（如 OTLP collector）。
5. **负载均衡**：API 与 `/mcp` 可无粘滞转发；`/ws/execution` 需 sticky session 或改为后端事件总线投递（待规划）。
6. **触发器去重**：启用 Quartz 集群模式或引入分布式锁，避免重复触发（待规划）。

## 5. 结论

- **今天**：单机单进程，开箱即用，零配置（SQLite）。
- **多机就绪**：换 PostgreSQL + 共享存储 + 一致密钥 + 外部日志即可起步。
- **后续工作**：执行引擎/触发器集群化、Worker 分离、分布式锁、WebSocket 多实例投递——属规划项，非当前已实现能力。

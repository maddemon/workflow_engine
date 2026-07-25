# 代码审计报告 2026-07-24（修订版）

> **修订说明**：本文件初稿由 DeepSeek 生成。后经第二轮 9 维度交叉审计（每个维度独立子代理深挖源码）逐条核对。本次修订：
> 1. **修正误判**：初稿称"无 SQL 注入风险""审计覆盖凭据/执行生命周期""CSRF 风险低"——经源码核实均不成立，已更正（见 SEC-0、OBS-1、OBS-2、S-4）。
> 2. **纠正过度表述**：初稿称"无健康检查端点"，实际存在 `/health` 静态桩但无 `AddHealthChecks`/探针，已校正（O-6）。
> 3. **补齐遗漏**：初稿缺失若干 High/Critical 级问题——DbReadNode 注入、ArrayPool 双重归还、`WorkflowExecutionWorker` 串行、事务跨外部 await、Shell 命令注入、全局鉴权兜底缺失、Webhook 无重放、凭据/执行审计事件未发布、DB 节点零日志、提交 DLL 入库、前后端枚举漂移、Core 硬编码凭据/触发类型、14 个冗余接口等。
> 4. **合并重复**：软删除过滤器、索引缺失、OTel 缺失等两版共识项合并，不重复罗列。
> 总体评分由 8.2 下调至 **7.1**（三轮审计合计：安全与可观测性暴露多项 High/Critical，数据层/代码质量/扩展性补强；mimo 误判项已更正、规范冲突项已标注不适用）。
>
> **第三轮（mimo）核对**：在 DeepSeek 与我方 9 维度审计基础上，纳入 mimo 的第三份评审并逐条复核。本次修订：
> - **更正我方误判**：第二节"优点"中"无 TODO"不成立——后端确有 **47 处 TODO（45 处 `TODO(i18n)`）**，已更正并补为技术债项（CQ-6/CQ-7）。
> - **更正 mimo 不准确项**：mimo 称"PluginLoader 无 AssemblyLoadContext 隔离""WebSocket 无独立认证"均不成立——`PluginLoader.cs:73` 确有 `PluginLoadContext`+SHA256+TFM；`ExecutionWebSocketHandler.cs:52` 经 `WebSocketAuthenticator` 返回 401。已在安全节标注。
> - **标注规范冲突**：mimo 架构 P2"Service 直注 DbContext 需引入仓储层"与本项目 AGENTS.md 后端规范 §6（"Service 直接注入 DbContext 为推荐做法、不强制 IRepository"）冲突，非缺陷，已标注不适用。
> - **补齐 mimo 独有发现**：`ServiceCollectionExtensions` 532 行膨胀、TriggerService 事务模板重复 3 处、`WorkflowSchedulerKernel` 887 行上帝类、凭据唯一索引 NULL 跨库不一致、调度补偿不完整（触发器侧）、JS 沙箱黑名单可绕过、启动全量回填、空闲 500ms 轮询、Workflow JSON 列 schema 演进困难、大 Service 测试缺口等。

---

## 目录

1. [架构与依赖治理](#1-架构与依赖治理)
2. [代码质量与整洁度](#2-代码质量与整洁度)
3. [数据层设计](#3-数据层设计)
4. [并发内存性能](#4-并发内存性能)
5. [异常与容错](#5-异常与容错)
6. [安全审计](#6-安全审计)
7. [可观测性](#7-可观测性)
8. [测试友好性](#8-测试友好性)
9. [迭代扩展性](#9-迭代扩展性)
10. [优先级修复路线图](#10-优先级修复路线图)
11. [附录：评分汇总](#11-附录评分汇总)

---

## 1. 架构与依赖治理

**评分：8.0 / 10**（初稿 8.5，下调：补充 DLL 入库、MediatR 入 Core 等依赖治理缺口）

### 依赖图（与初稿一致，已核实为严格 DAG，无循环依赖）

```
Core (零外部引用)
  ← Runtime (依赖 Core)
  ← Application (依赖 Core + Runtime)
  ← Infrastructure (依赖 Core + Application)
  ← Migrations (依赖 Core)
  ← Host (组合根)
  ← Plugins (仅依赖 Core)
```

### 优势（核实保留）

- Core 层零外部引用，符合 Clean Architecture 最内层约束。
- Runtime 层无反向引用；插件经 `AssemblyLoadContext` 隔离、仅引用 Core、SHA256 白名单校验。
- 14 个 Controller 均不注入 `DbContext`，仅调用 Application Service。
- DI 生命周期合理：无状态服务 Singleton，DbContext 相关 Scoped（但见 SEC-2 全局鉴权兜底缺失）。

### 问题与建议

| # | 问题 | 严重度 | 建议 |
|---|------|--------|------|
| A-1 | **Application → Runtime 直接引用具体类型**：`ExecutionService`/`WorkflowDryRunService` 直接引用 `Runtime.Executor` 的 `ExecutionCancellationRegistry`、`SecretMasker` | ⚠️ 中 | 在 Core `Abstractions/` 定义 `IExecutionCancellationRegistry`/`ISecretMasker`，Application 经接口依赖 |
| A-2 | **Infrastructure → Application 反向实现接口**：`IUserStore`/`ITokenService`/`IPasswordHasher`/`IAuditLogReader` 定义于 Application，由 Infrastructure 实现 | ⚠️ 中 | 跨层契约迁移到 Core `Abstractions/` 或新建 `FlowEngine.Contracts` |
| A-3 | **Jint 重复引用**：Core 与 Runtime 均显式引用 `Jint 4.11.0` | 🟡 低 | 移除 Runtime 显式引用，经 Core 传递 |
| A-4 | **`NodeExecutionContext` 上帝对象**：456 行、25+ 属性，兼具数据载体+服务定位器 | ⚠️ 中 | 仅保留数据载体，工具方法提取为 Service/扩展 |
| A-5 | **`WorkflowService` 过大**：459 行，CRUD+草稿+统计+触发器同步 | 🟡 低 | 拆分草稿/统计为独立 Service |
| A-6 | **`NodeExecutionContextFactory` 构造函数 12 参数** | ⚠️ 中 | Options 聚合可选依赖 |
| **DEP-1** | **构建产物（DLL）提交入库（High）**：`plugins/` 含 206 个三方二进制（AWSSDK/MongoDB.Driver/StackExchange.Redis 等）；`Host.csproj` 的 `BuildPlugins` 目标把插件 DLL 拷回 `plugins/`，形成 VCS↔构建回路，且将未审计三方二进制纳入源码控制 | 🔴 High | `.gitignore` 加入 `plugins/*.dll`、`*.pdb`、`bin/`、`obj/`；构建期生成而非提交；对原生二进制做供应链扫描 |
| **DEP-2** | **MediatR 进入 Core**：`Core/Events/MediatrEventBus.cs` 引用 `MediatR 12.4.1`，违反"Core 仅依赖 EF Core+日志+Jint" | ⚠️ 中 | 事件总线下沉到 Runtime/Infrastructure，或正式修订架构规则（MediatR 12.x 为 MIT，13+ 商用） |
| **DEP-3** | **双 JSON 框架**：`Audit.NET`(Newtonsoft) 与 `System.Text.Json` 并存（`Host` 确认 Newtonsoft 为共享程序集） | ⚠️ 中 | 统一 `System.Text.Json`；Audit.NET 若强依赖 Newtonsoft 则隔离在适配器后 |
| **DEP-4** | **插件 DB 节点并行引入 Dapper+多驱动 ADO.NET 栈**：与"DB 访问统一走 EF Core/DbContext"规则矛盾，扩大维护/攻击面 | ⚠️ 中 | 在 Core 定义统一 DB 抽象；SQL 校验/白名单集中一处 |
| **DEP-5** | **前端依赖版本异常**：`lucide-react ^1.21.0` 非可解析版本线；Mantine 套件次版本漂移（`@mantine/core ^9.3.2` vs `@mantine/form ^9.4.1`） | 🟡 低 | 校正版本范围并 `npm audit`；对齐 Mantine 版本 |
| **DEP-6** | **未验证/预发布依赖**：`DM.Microsoft.EntityFrameworkCore`(达梦，EF9 构建于 net10/EF10 项目，版本错配风险)、`ModelContextProtocol.AspNetCore 2.0.0-preview.2` 进入组合根 | ⚠️ 中 | 确认 EF10 兼容或升级；预发布包固定稳定版或功能开关隔离 |
| **DEP-7** | **`ServiceCollectionExtensions` 过度膨胀（532 行）**：全部 DI 注册集中于单一文件/方法，新增 Service 必改此文件（mimo 架构 P1/P9-P1） | ⚠️ 中 | 按模块拆分为 `AddFlowEngineWorkflow()`/`AddFlowEngineCredentials()` 等扩展方法，或用模块化注册 |

### 与 mimo 第三轮审计的核对（架构维度）

- **mimo P2（Application 直注 `FlowEngineDbContext`=缺陷，建议引仓储层）——不适用**：本项目 AGENTS.md 后端规范 §6 明确规定"Service 直接注入并使用 DbContext 读写（推荐做法）、不强制定义 IRepository"，此为有意设计，非缺陷。保留为"已知取舍"，不列为问题。
- **mimo P3（WebSocketEventPushService Singleton 持有 `ConcurrentDictionary` 可变状态）——记录为 Low 信息项**：经核实该类持有 `WebSocketConnectionManager`/`WebSocketReplayService` 均为 Singleton，无 Scoped 捕获，当前安全；仅需在并发写入路径保持锁一致（见 CON-7）。
- mimo 对分层 DAG、PluginLoader ALC 隔离、DbContext Scoped 注册的正面评价与本轮一致。

---

## 2. 代码质量与整洁度

**评分：7.5 / 10**（保留初稿评分；补充后端遗漏项）

### 优势

- 后端命名规范一致；前端零 `any`；结构化日志模板一致；ahooks `useRequest` 广泛使用。
- 已核实：**无** `Console.WriteLine`、**无** `.Result`/`GetAwaiter().GetResult()` 阻塞、**无** async void、Fluent API 仅用于 JSON 列/程序化索引、实体 `///`+`[Comment]` 齐全。
- **更正初稿"无 TODO"结论**：后端经 grep 确认存在 **47 处 `TODO`**（其中 45 处为 `TODO(i18n)`，分布在 `WorkflowModificationService`/`WorkflowAssemblyService`/`CredentialService`/`FileService`/`WebhookRouteService` 等），均为"错误消息硬编码中文、待注入 `IStringLocalizer` 本地化"的技术债，非阻塞但需跟踪（见 CQ-6）。

### 问题与建议

| # | 问题 | 严重度 | 建议 |
|---|------|--------|------|
| Q-1 | **前端零 CSS Modules**：474 行全局 `App.css` | 🔴 高 | 按组件迁移到 CSS Modules |
| Q-2 | **前端 100+ 行内样式** | 🔴 高 | 提取为 CSS 类/Mantine 属性 |
| Q-3 | **前端零 Error Boundary** | 🔴 高 | 包裹 Canvas/ParameterPanel/ExecutionPanel |
| Q-4 | **`cancelExecution` 缩进结构 Bug**（`useExecution.ts:195-222`） | ⚠️ 中 | 重构并补测试 |
| Q-5 | **Props 接口命名不一致**（`INodePanelProps` vs `FieldResolverProps`） | 🟡 低 | 统一规范 |
| Q-6 | **`WorkflowEditorPage` useEffect 缺依赖**（`eslint-disable` 抑制） | 🟡 低 | `useRef` 稳定引用或注释说明 |
| Q-7 | **`ScriptCache.TrimIfNeeded` 竞态**：`_cache.Clear()` 在锁外 | 🟡 低 | 移入 `lock(_trimLock)` |
| **CQ-1** | **14 个 Application 服务接口单一实现即冗余**（违反"单实现直接写类"）：`IWorkflowService` 等 14 个接口仅一个实现，`WorkflowsController` 直接注入具体类，接口纯冗余 | ⚠️ 中 | 删除内部单实现接口，仅保留跨程序集/需 mock 的 Core `Abstractions` 接口 |
| **CQ-2** | **`DbReadNode` SQL 扫描器复制**：`--`/`/* */`/引号跳过逻辑在 `HasTrailingStatement`/`ExtractFirstKeyword`/`ContainsKeyword` 三处重复 | ⚠️ 中 | 抽取单一 SQL tokenizer 复用 |
| **CQ-3** | **前端硬编码 API URL**：`useWebSocketExecution.ts:32` 内置 `/api/v1/executions/.../stream`，绕过 `services/api.ts` 集中管理 | 🟡 低 | URL 构造移入 `services/api.ts` |
| **CQ-4** | **`TriggerService` 事务模板重复 3 处**：`TriggerService.cs:68-85`、`:204-221`、`:295-310` 完全相同 `IsRelational()`→`BeginTransaction`→`SaveChanges`→`Commit`→`catch{Rollback;throw;}` | ⚠️ 中 | 抽取 `SaveChangesInTransactionAsync()` 通用方法 |
| **CQ-5** | **`WorkflowSchedulerKernel` 上帝类（887 行）**：调度循环、节点处理、输出路由、超时、等待区全在一个类（mimo 代码质量 P3） | ⚠️ 中 | 拆分为调度循环/节点执行器/输出路由/等待区等独立组件（与 CON-2/CON-6 协同） |
| **CQ-6** | **47 处 `TODO(i18n)` 技术债**：错误消息硬编码中文，待注入 `IStringLocalizer` 本地化（mimo 代码质量 P2） | 🟡 低 | 规划国际化阶段统一替换；当前不影响功能 |
| **CQ-7** | **`WorkflowExecutionFeedbackService` 残留 TODO**：`:50-51` `NodeName = nodeRecord.NodeDefinitionId`（附 `TODO: resolve display name`），类型名显示为空 | 🟡 低 | 解析节点显示名或从定义取 `Name` |

---

## 3. 数据层设计

**评分：7.0 / 10**（保留；补充事务/分页/索引 High 项）

### 优势

- UUIDv7 主键、JSON 列策略（`[JsonColumn]`+反射 Fluent+提供者感知）精良、`AsNoTracking()` 一致使用、`WorkflowStatisticsLoader` 批量查询、迁移含 `AddHotQueryIndexes`。
- 写入路径使用方言 SQL 生成器 + `@pN` 命名参数（DbUpsert）；删除用 `ExecuteDeleteAsync` 不物化整行。

### 问题与建议

| # | 问题 | 严重度 | 建议 |
|---|------|--------|------|
| D-1 | **缺软删除全局过滤器**：所有实体有 `Deleted` 但无 `HasQueryFilter(e => !e.Deleted)`（已核实不存在），易漏查已删数据 | 🔴 P0 | `OnModelCreating` 统一加过滤器，需访问已删数据处显式 `IgnoreQueryFilters()` |
| D-2 | `triggers.workflow_definition_id` 缺索引 | 🔴 P1 | 加迁移建索引 |
| D-3 | `triggers.project_id` 缺索引 | ⚠️ P1 | 加迁移建索引 |
| D-4 | `stored_files.project_id` 缺索引 | ⚠️ P1 | 加迁移建索引 |
| D-5 | Schema 命名不一致（`Trigger`/`WebhookRoute`/`User` 用默认 schema） | ⚠️ P2 | 统一 `flow` schema |
| D-6 | `WorkflowService.GetAllAsync` 加载完整 `Nodes`/`Connections` JSON 列 | ⚠️ P2 | `.Select()` 投影到 `WorkflowSummaryDto` |
| D-7 | `ExecutionRecord.NodeRecords` 无界 JSON | 🟡 P3 | 评估拆 `NodeExecutionRecord` 子表 |
| D-8 | SQLite 独占迁移 | 🟡 P3 | 多库迁移目录；清理用 EF 方法 |
| D-9 | `MigrateAsync()` 每次启动运行 | 🟡 P3 | 仅 Development 自动迁移，生产走 CI/CD |
| **D-10** | **事务跨外部 await（High）**：`WorkflowService.cs:163-204` 的 `BeginTransactionAsync` 跨越 `RegisterTriggersAsync`（Quartz 调度）才提交，行锁长时间持有；且调度异常被 catch-记录后事务仍提交，状态不一致 | 🔴 High | 先提交 DB 写再调外部调度；若需原子性改用 outbox/saga |
| **D-11** | **执行列表查询拉取大 JSON 列（High）**：`ExecutionService.GetByWorkflowAsync` 物化整条 `ExecutionRecord`（含可能极大的 `NodeRecords`）再投影 `ExecutionSummaryDto` | 🔴 High | `Select(...)` 仅取摘要字段；`NodeRecords` 评估拆子表/设上限 |
| **D-12** | **OFFSET 分页深翻页退化**：`ExecutionService` 用 `Skip/Take`，大表深翻页扫描丢弃行 | ⚠️ 中 | 改 keyset 分页（`WHERE StartedAt < lastSeen ORDER BY StartedAt DESC`） |
| **D-13** | **`ExecutionRecord.StartedAt` 排序无索引**：列表按 `StartedAt` 降序，仅存在 `(Status,CompletedAt)` 索引 | 🟡 低 | 加 `[Index(nameof(StartedAt))]` 或 `(WorkflowDefinitionId, StartedAt)` |
| **D-14** | **`SaveChangesAsync` 每次保存触发额外查询（mimo 数据 P1）**：`FlowEngineDbContext.cs:52-95` 重写中扫描 ChangeTracker 变更的 Workflow，执行 `ToListAsync` 查询 + `RemoveRange` + `AddRange` 维护 `WorkflowCredentialUsages`；批量改 Workflow 时放大为 N 次额外查询 | ⚠️ 中 | 仅对当前变更 Workflow 的凭据用量做增量维护；批量场景分页/过滤 |
| **D-15** | **`Credential` 唯一索引 `(Name, ProjectId)` NULL 跨库不一致（mimo 数据 P2）**：`FlowEngineDbContext.cs:102` `ProjectId` 允许 NULL；SQLite 允许多个 `(name, NULL)`，PostgreSQL 不允许——开发与生产行为分歧 | ⚠️ 中 | 统一语义：用 sentinel 值替代 NULL，或按 DB 提供方条件化唯一索引；集成测试覆盖双库 |
| **D-16** | **`WorkflowCredentialUsageBackfill` 启动全量回填（mimo 数据 P3）**：`ServiceCollectionExtensions.cs:231` 托管服务启动时可能执行大量回填 | 🟡 低 | 改为后台分批/幂等回填，或仅在数据不一致时触发；避免冷启动阻塞 |

---

## 4. 并发内存性能

**评分：8.0 / 10**（初稿 8.5，下调：补入真实正确性与吞吐缺陷）

### 优势

- `ConcurrentDictionary` 在 `NodeRegistry`/`ExecutionSession`/`WaitingArea` 一致使用；`CancellationToken` 贯穿；`ScriptCache` LRU 4096 上限；JsEngine `finally` 中 `ReleaseEngine()`；`PluginLoadContext` `isCollectible: true`。
- 注：当前单线程执行模型下并发复杂度低，但见 CON-3——并行化前必须先解决可变单例问题。

### 问题与建议

| # | 问题 | 严重度 | 建议 |
|---|------|--------|------|
| C-1 | `Task.Run` 包裹同步 Jint 执行 | 🟡 低 | 监控线程池饥饿 |
| C-2 | `HttpClientPool` 无界实例 | 🟡 低 | `ObjectPool<HttpClient>` 或 `IHttpClientFactory` |
| C-3 | `ExecutionSession` 持有完整工作流内存 | 🟡 低 | 评估懒加载节点定义 |
| C-4 | `JsEngine.RunAsync` 硬编码 5s 超时 | 🟡 低 | 统一 `ExecutionTimeoutMs` 配置 |
| **CON-1** | **ArrayPool 双重归还（High 正确性 Bug）**：`ExecutionWebSocketHandler.cs:131,158` 同一 buffer 在 Close 分支与外层 `finally` 各 `Return` 一次，污染共享池（后续租户可能拿到活数据） | 🔴 High | 移除提前 `Return`，由 `finally` 独占归还；或加 `returned` 标志 |
| **CON-2** | **工作流全局串行执行（High）**：单 `BackgroundService` 一次只处理一个执行，慢/LLM 节点阻塞全部 | 🔴 High | 有界 `SemaphoreSlim`/`Task.WhenAll` 并发消费队列（执行作用域已隔离） |
| **CON-3** | **节点类型实例为可变单例（潜在 High）**：`NodeRegistry` 缓存单例，`SwitchNode.Cases`/`Ports` 等可变状态；当前串行安全，但并行化或重入子流程会相互篡改共享状态 | ⚠️ 中→高(并行化后) | 使节点类型无状态或按执行克隆，执行中禁止改共享单例字段 |
| **CON-4** | **`OAuth2TokenService` TOCTOU 惊群**：并发调用均见过期并同时刷新令牌 | ⚠️ 中 | per-key `SemaphoreSlim`/lazy-cache 去重刷新 |
| **CON-5** | **大批次内存无界**：`SuccessfulOutputs`/`OncePerItem` 在整次运行期间累计持有全部节点输出，大 `OncePerItem` 输入常驻内存 | ⚠️ 中 | 流式/分块输出，限制保留项数或增量落盘 |
| **CON-6** | **`WorkflowSchedulerKernel` 空闲轮询 500ms（mimo 并发 P2）**：`:53`/`82` 调度循环 `Task.Delay(IdleDelayMilliseconds=500)` 空轮询，高吞吐增加延迟、空耗线程 | ⚠️ 中 | 改用 `SemaphoreSlim`/`TaskCompletionSource` 事件驱动唤醒，消除忙等 |
| **CON-7** | **`WebSocketEventPushService` 广播迭代 `ConcurrentDictionary`（mimo 并发 P3）**：`:320-355` 广播时遍历连接字典，高并发下迭代成本与快照一致性需注意 | 🟡 低 | 广播路径加锁/快照；结合 CON-3 单例可变状态保持一致 |

### 与 mimo 第三轮审计的核对（并发维度）

- mimo P1 关切 `ScriptCache.RecordAccess` 并发操作 `_orderIndex`——经核实 `RecordAccess` 与 `TrimIfNeeded` 均在 `_trimLock` 保护下（仅 `_cache.Clear()` 在锁外，见 Q-7），故并非"两锁不统一"；当前实现基本正确，保留 Q-7 为 Low 信息项。
- mimo 对 `WorkflowExecutionQueue`(Channel 无锁)、`ScriptCache` LRU、每执行独立 Scope、`ExecutionCancellationRegistry` 的正面评价与本轮一致。

---

## 5. 异常与容错

**评分：7.5 / 10**（保留；补充基类与泄露项）

### 优势

- 统一异常中间件 `{success,errorCode,message,details}` 形状正确、敏感信息过滤到位；Controller 无逐 action try/catch；插件加载失败仅 warn 且不影响启动；节点级错误隔离 + 有界退避重试合规；已核实 C# 侧**无**吞异常（`catch{}`/空 catch 仅出现于前端压缩包）。

### 问题与建议

| # | 问题 | 严重度 | 建议 |
|---|------|--------|------|
| E-1 | **审计序列化失败静默丢事件**：`AuditLogFileSink.SerializeEvent()` `catch { return null; }` | 🔴 P0 | `catch` 内至少 `LogError`，失败事件写死信队列 |
| E-2 | `ScriptErrorException` 未在全局异常处理器映射 | ⚠️ 中 | 加 `ScriptErrorException → 400` 映射 |
| E-3 | Webhook 错误响应格式不一致（返回 `{error}`） | ⚠️ 中 | 统一标准格式或文档标注 |
| E-4 | 无请求日志中间件 | ⚠️ 中 | 加 `RequestLoggingMiddleware`（排除 `/health`） |
| E-5 | 无 `LogCritical` 使用 | 🟡 低 | 定义场景：连接池耗尽/插件全失败/密钥不可用 |
| E-6 | `ToolContextFactory` 仅记 `ex.Message` | 🟡 低 | 改 `LogWarning(ex, ...)` 记录完整异常 |
| E-7 | `AuditLogReader` 损坏 NDJSON 行静默跳过 | 🟡 低 | `LogWarning` 记录行号与前 100 字符 |
| **EX-1** | **无 `DomainException` 基类**：6 个业务异常直接继承 `Exception`，中间件逐个枚举类型映射，与"领域异常继承 `DomainException`"规则冲突 | ⚠️ 中 | 引入 `DomainException : Exception`，中间件按基类映射 |
| **EX-2** | **`NodeError` 泄露 `StackTrace`+原始 `ex.Message` 给客户端**：`WorkflowSchedulerKernel.cs:490-492`，经执行结果/事件暴露表名/路径 | ⚠️ 中 | 仅保留 `Message` 供运维日志；客户端侧用安全错误码，不回显原始堆栈 |
| **EX-3** | **触发器更新后调度补偿不完整（mimo 异常 P1）**：`TriggerService.cs:226-237` 事务提交成功后，若注册 Quartz 调度失败仅 `LogError` 不回滚——触发器已 Active 但调度未恢复，永久"静默失效" | 🔴 High | 与 D-10 同源：先完成 DB 提交并确认调度注册成功，失败则进入补偿/告警；或 outbox 重放 |
| **EX-4** | **Webhook 同步模式数据库轮询等待（mimo 异常 P2）**：`WebhookHandler.cs:166-192` `IsSync` 模式循环查 `ExecutionRecords` 等待完成，每次循环都打 DB 查询 | ⚠️ 中 | 改用事件通知/WebSocket 推送完成事件，避免轮询 |

### 与 mimo 第三轮审计的核对（异常维度）

- mimo P3 称"全局 62 处 `catch (Exception)`，部分可能吞没"——本轮已逐一审计：**C# 后端无静默吞没**（`catch{}`/空 catch 仅出现在前端压缩包）；后端 `catch (Exception)` 均配 `Log`+rethrow 或 `continue`，属合规容错。此项**非确认缺陷**，保留为"已验证"结论，不再单列。
- mimo 对异常体系（`NotFoundException`/`PermissionDeniedException` 等语义清晰）、全局中间件、执行引擎 `ErrorStrategyHandler` 重试策略的正面评价与本轮一致。

---

## 6. 安全审计

**评分：7.5 / 10**（初稿 8.5，下调：修正"无 SQL 注入"误判、CSRF 升级、补 Shell 注入/全局鉴权/Webhook 重放等）

### 优势（保留并校正）

- 双方案认证（JWT Bearer + API Key）`PolicyScheme` 路由；Cookie `HttpOnly`+`Secure`(生产)+`SameSite=Lax`；三层 RBAC；凭据 AES-256-GCM + 密钥版本化；密码 PBKDF2/Argon2 + 复杂度；登录 5 次/15 分锁定 + 时序防护；Webhook HMAC-SHA256 + `FixedTimeEquals` + IP/Origin 白名单；Jint 沙箱（内存/语句/递归限制、无 CLR）；安全头齐全；CORS 未配置时默认拒绝。
- **已更正初稿误判**：初稿称"无 SQL 注入风险""无硬编码密钥"——实际 **DbReadNode 存在注入风险（SEC-0）** 且 **构建产物含 dev JWT 密钥（SEC-7）**；"API 主要用 Bearer 头、CSRF 风险低"亦不准确（见 S-4）。

### 问题与建议

| # | 问题 | 严重度 | 建议 |
|---|------|--------|------|
| **SEC-0** | **DbReadNode SQL 注入（Critical）**：`DbReadNode.cs:129` 以 `ExecuteReaderAsync(sql, null, …)` 执行，`null` 参数；SQL 由 `ResolveSqlAsync` 经 Jint 表达式拼接生成，可嵌入上游 `$input`/`$json` 值；只读校验只是关键字扫描（`IsReadOnlyStatement`），跨方言/混淆脆弱。无绑定参数路径（对比 DbUpsert 的 `@p0..`） | 🔴 Critical | 提供绑定参数（从 `$input`/`$json` 解析为 `@pN`），dbRead 与 dbUpsert 统一参数化；禁止将上游值字符串拼入 SQL；或改用只读 DB 角色 |
| S-1 | Token 黑名单在内存（`IMemoryCache`），重启/多实例丢失 | ⚠️ 中 | 迁 Redis/DB，或缩短 JWT+Refresh Token |
| S-2 | 登录锁定状态在内存 | ⚠️ 中 | 迁 Redis/DB |
| S-3 | Webhook 密钥明文存储 | ⚠️ 中 | 复用 `CredentialEncryptionService` AES-GCM + `KeyVersion` |
| **S-4** | **CSRF（升级为 Medium→High）**：`AuthController.cs:75` Cookie `SameSite=Lax`；前端 `api.ts:39` 实际 `withCredentials: true`，即 API 调用依赖 Cookie 认证，恶意站点可发起变更请求。初稿称"主要用 Bearer、风险低"不成立 | 🔴 高（修正） | 设 `SameSite=Strict` 或增自定义防伪造头/ Antiforgery；收紧 CORS origin |
| S-5 | API Key 验证无缓存 | 🟡 低 | `IMemoryCache` 缓存 key_hash→用户映射（TTL 5 分） |
| S-6 | `AllowedHosts: "*"` | 🟡 低 | 限制已知域名 |
| S-7 | WebSocket `/ws/execution` 无显式 `[Authorize]`（依赖握手） | 🟡 低 | 加中间件/属性，认证失败即断连 |
| **SEC-1** | **ShellToolNode 命令注入（High）**：`RunInShell=true` 时命令经 `bash -c`/`powershell`/`cmd /c` 执行，命令由 LLM/Agent 可控的工作流输入拼接 | 🔴 High | `RunInShell` 置于管理员权限门后；开启时拒绝含不可信插值或严格白名单；LLM 可控命令考虑直接禁用 |
| **SEC-2** | **无全局鉴权兜底**：鉴权仅靠逐端点 `[Authorize]`，新增端点/最小 API 漏标即匿名暴露 | ⚠️ 中 | 设 `FallbackPolicy = RequireAuthenticatedUser()` 或程序集级 `[Authorize]` |
| **SEC-3** | **Webhook 无重放保护/限速**：HMAC 无 timestamp/nonce 绑定，捕获请求可无限重放；无按路由/IP 限速 | ⚠️ 中 | HMAC 绑定 timestamp+nonce，拒绝过期/已见；加 `AspNetCoreRateLimit`；无幂等键时告警 |
| **SEC-4** | **`FilesController` 信任客户端 ContentType**：`File(stream, metadata.ContentType, …)` 回传攻击者可控 MIME，无 `nosniff`/附件头 | 🟡 低 | 服务端校验/归一化类型；加 `X-Content-Type-Options: nosniff` 与 `Content-Disposition: attachment`；考虑上传 AV 扫描 |
| **SEC-5** | **DbReadNode 只读校验脆弱**：关键字扫描易被方言/注释混淆绕过（与 SEC-0 关联） | 🟡 低 | 解析器或 DB 原生只读角色；禁止不可信值插值 |
| **SEC-6** | **密钥/构建产物泄露风险**：`bin/**/appsettings.Development.json` 含真实 dev JWT 密钥（源文件为占位符，若 bin 被跟踪即泄露）；Dev `data/crypto.key` 明文文件若流入生产即全量解密 | 🟡 低 | 确保 `bin/obj/data/` gitignore；轮换 dev 密钥；非 Development 加载文件密钥告警 |
| **SEC-7** | **JS 沙箱标识符黑名单可绕过（mimo 安全 P2）**：`JsEngineOptions.cs:8-15` `ForbiddenIdentifiers` 仅检查标识符 token，可通过字符串拼接 `this['cons'+'tructor']`、Unicode 同形异义字、`obj['pro'+'cess']` 属性链绕过黑名单 | ⚠️ 中→高 | 改白名单模式（仅放行必要 API）或换 V8 Isolate 强隔离；黑名单仅作纵深防御之一环 |

### 与 mimo 第三轮审计的核对（安全维度）

- **mimo P3"PluginLoader 无 AssemblyLoadContext 隔离"——不成立**：`Runtime/Registry/PluginLoader.cs:73` 确有 `new PluginLoadContext(dllPath)`（即 `AssemblyLoadContext` 子类，`isCollectible: true`）+ SHA256 哈希白名单 + TFM 兼容性检查。mimo 仅因未定位到该类而误判，已更正。
- **mimo P4"WebSocket 端点无独立认证"——不成立**：`ExecutionWebSocketHandler.cs:33,52-54` 经 `WebSocketAuthenticator` 校验，未认证返回 401；非无认证。与 SEC-2/S-7 中"WebSocket 已鉴权"结论一致。
- mimo P1（Dev 明文密钥）、P2（沙箱绕过，已升 SEC-7）为本轮采纳；其余正面评价（AES-256-GCM、FixedTimeEquals、RBAC、CORS 默认拒绝、安全头）与本轮一致。

---

## 7. 可观测性

**评分：6.0 / 10**（初稿 6.5，下调：补入审计事件未发布、DB 零日志等 Critical/High，并校正"审计覆盖"误判）

### 优势（保留并校正）

- 结构化日志模板一致、级别合理、零 `Console.WriteLine`、敏感值不落日志。
- **已更正初稿误判**：初稿称"审计覆盖工作流 CRUD、触发器、执行生命周期、凭据操作、权限拒绝"。经核实：`CredentialAccessedEvent` **从不发布**（凭据访问无审计），`WorkflowStartedEvent`/`NodeExecutedEvent`/`NodeErrorEvent` **有定义但从不发布**（执行开始/节点完成/节点错误无审计）。实际仅覆盖 CRUD、完成/失败/取消、节点开始——**执行生命周期与凭据访问的审计链不完整**。

### 问题与建议

| # | 问题 | 严重度 | 建议 |
|---|------|--------|------|
| O-1 | 无结构化日志接收端（仅 Console） | ⚠️ 高 | 集成 Serilog → Seq/ES；保留 Console |
| O-2 | 无 OpenTelemetry/分布式追踪 | ⚠️ 高 | 集成 OTel，ASP.NET Core + HttpClient 仪表，导出 Jaeger/Tempo |
| O-3 | 审计序列化失败静默丢事件（见 E-1） | 🔴 高 | 见 E-1 |
| O-4 | 无请求日志中间件（见 E-4） | ⚠️ 中 | 见 E-4 |
| O-5 | 无 `LogCritical`（见 E-5） | 🟡 低 | 见 E-5 |
| O-6 | 健康检查（校正）：`/health` 端点**存在**（`ApplicationBuilderExtensions.cs:85`）但是静态桩——无 `AddHealthChecks`、无就绪/存活探针、无 DB/依赖探活；`Program.cs` 无 `Meter`/OTel | 🟡 低（存在但残缺） | 升级为 `AddHealthChecks` + liveness/readiness 分离 + DB 探针；加执行/节点/失败 `Meter` |
| **OBS-1** | **凭据访问无审计（Critical）**：`CredentialAccessedEvent` 仅定义+注册 `AuditEventNotificationHandler`，运行时从未 `Publish`（凭据解析路径缺失） | 🔴 Critical | 在凭据运行时解析处 `PublishAsync(new CredentialAccessedEvent(...))` |
| **OBS-2** | **执行关键事件未发布（High）**：`WorkflowStartedEvent`/`NodeExecutedEvent`/`NodeErrorEvent` 有定义但执行器从不发布，审计链缺执行开始/节点完成/节点错误 | 🔴 High | `WorkflowExecutor` 中补充发布上述事件 |
| **OBS-3** | **DB 节点执行零日志（High）**：`plugins/.../DbExecutor.cs` 无 SQL/行数/耗时日志，失败不打印 SQL 文本，诊断困难 | 🔴 High | 记（脱敏）SQL+影响行数+耗时；错误包含 SQL 文本（注意不泄露参数值） |
| **OBS-4** | **TraceId 不贯穿执行**：`TraceId` 仅异常中间件有，执行日志用 `executionId` 但无 `ActivitySource`，HTTP→执行无法关联 | ⚠️ 中 | 每执行起 `Activity`，从入站 HTTP 传播 `TraceParent`，日志统一带 `executionId`+`traceId` |
| **OBS-5** | **Webhook 路由/trigger 查找失败无日志无审计**：返回 404 无记录 | ⚠️ 中 | `LogWarning` + 审计事件 |
| **OBS-6** | **前端错误无遥测**：`globalErrorHandler.ts` 仅 `console.error`/通知，无关联 ID/路由上下文，无用户行为遥测 | 🟡 低 | 附带 route/user id 上报遥测 |
| **OBS-7** | **`WebSocketEventPushService` 广播失败日志不足（mimo 可观测 P4）**：`:320-355` 广播失败仅 `LogWarning`，无结构化指标（成功/失败数、连接数、延迟） | 🟡 低 | 增加结构化日志与 Meter 计数（结合 O-2/OBS 指标项） |

---

## 8. 测试友好性

**评分：8.0 / 10**（保留；补充后端分支保护缺口）

### 优势

- 5 测试项目 +1 测试插件、命名规范、`WebApplicationFactory` 集成测试、手写 Fake 优于 Moq、插件节点覆盖全面、安全测试充分、边界测试良好。

### 问题与建议

| # | 问题 | 严重度 | 建议 |
|---|------|--------|------|
| T-1 | 前端零测试 | 🔴 高 | 搭 Vitest + RTL，优先工具函数/Store |
| T-2 | ~90% 用 InMemory 库，不测 SQL/约束/索引 | ⚠️ 中 | 关键查询补 SQLite 集成测试 |
| T-3 | 无真实库（PG/SQLServer）集成测试 | ⚠️ 中 | CI 加 PG 容器测试 |
| T-4 | 无负载/并发测试 | ⚠️ 中 | `ExecutionServiceConcurrencyTests` 并发提交 100+ |
| T-5 | `ScriptCacheTests` 反射私有字段 | 🟡 低 | 改为公共行为验证 |
| T-6 | `NullCredentialAccessor` 重复定义 6+ 处 | 🟡 低 | 提取共享 Helper |
| T-7 | `xUnit1051` 警告被抑制 | 🟡 低 | 查源修复 |
| T-8 | 无前后端 DTO 契约测试 | 🟡 低 | OpenAPI 生成 + TS 兼容校验 |
| **TST-1** | **IfNode 比较分支未测（High）**：`>`/`<`/`>=`/`Contains`/`StartsWith` 在 C# 层无测试，路由语义依赖 Jint | 🔴 High | 按比较符补 `IfNodeTests`，断言 `BranchIndex` 0/1 |
| **TST-2** | **事务回滚未测（High）**：仅 happy-path CRUD，无失败回滚验证 | 🔴 High | 强制 `SaveChangesAsync` 抛异常，断言状态回滚 |
| **TST-3** | **`ParameterResolver` 直接依赖具体 `ScriptCache`（无 `IScriptCache`）**，单元测试难隔离 | ⚠️ 中 | 抽取 `IScriptCache` 注入 |
| **TST-4** | **`PluginLoader` 各异常回退未逐一验证**：仅覆盖一种失败形态 | ⚠️ 中 | 逐异常类型补"坏 DLL 跳过、好 DLL 仍加载"测试 |
| **TST-5** | **大 Service 测试缺口（mimo 测试 P1）**：`WorkflowService.cs`(~460 行) 仅少量 CRUD/版本测试；`WorkflowModificationService.cs`(500+ 行) 无独立测试；集成测试数量有限、缺 API 层端到端 | ⚠️ 中 | 补草稿确认/拒绝、统计、触发器同步等分支测试；增 `WebApplicationFactory` 端到端覆盖核心 API |

---

## 9. 迭代扩展性

**评分：7.5 / 10**（初稿 8.0，下调：补入枚举漂移、Core 硬编码类型等 High 瓶颈）

### 优势

- 插件系统（`INodeType`+ALC+SHA256+TFM）精良；事件总线双调度；`IExecutionSideEffects` 策略；`FieldResolver` 三级分发；多库架构可切换。

### 问题与建议

| # | 问题 | 严重度 | 建议 |
|---|------|--------|------|
| X-1 | Application→Runtime 直接引用限制扩展（见 A-1） | ⚠️ 中 | 见 A-1 |
| X-2 | 接口定义位置不一致 | 🟡 低 | 跨层契约归 Core `Abstractions/` |
| X-3 | `NodeExecutionContext` 上帝对象（见 A-4） | ⚠️ 中 | 见 A-4 |
| X-4 | 前端零 CSS Modules（见 Q-1） | ⚠️ 中 | 见 Q-1 |
| X-5 | SQLite 独占迁移（见 D-8） | 🟡 低 | 见 D-8 |
| **EXT-1** | **前后端枚举漂移（High）**：后端 `ParameterType` 含 `Script`，前端 union 为 `Expression`（无 `Script`）；前端另有 `DateTime`/`Select` 后端无——两来源不同步 | 🔴 High | 由 Core 枚举生成 TS 类型（或共享契约文件）+ CI 一致性测试 |
| **EXT-2** | **凭据/触发类型硬编码于 Core（High）**：`CredentialTypeRegistry.cs:76-161` 内置类型硬编码，`Validate` 硬编码 OAuth2 提供方；`TriggerType` 为固定枚举 + 扁平 `TriggerSettingsDto`。新增即需改 Core，违背插件化 | 🔴 High | 类型/提供方改为注册表或插件驱动；触发配置改为每类型对象 + handler 注册表 |
| **EXT-3** | **`NodeTypesController` 直接返回 Core 实体** `NodeTypeDescriptor`（违反"返回 DTO 非实体"） | ⚠️ 中 | 映射 `NodeTypeDescriptorDto` |
| **EXT-4** | **新 `ParameterType`/`PresentationHint` 需在两个 map 注册**（`hintFieldMap`/`typeFieldMap`），线性增长 | ⚠️ 中 | 改为按枚举键的注册表，去重 |
| **EXT-5** | **Workflow 节点/连接以 JSON 列存储，schema 演进困难（mimo 扩展 P2）**：`Nodes`/`Connections` 为 JSON 列，节点结构变更需处理历史数据兼容，无法对节点字段做库级查询/索引 | ⚠️ 中 | 评估高频查询字段抽取为关系列；提供 JSON schema 版本化与迁移脚本 |
| **EXT-6** | **脚本语言仅支持 JavaScript（mimo 扩展 P3）**：`ScriptLanguage.cs` Python 标记"预留，暂不支持" | 🟡 低 | 若需多语言，抽象 `IScriptEngine` 注册表，按语言加载隔离引擎 |

---

## 10. 优先级修复路线图

### P0 — 立即修复（数据安全/正确性/可观测性）

| ID | 问题 | 影响模块 |
|----|------|---------|
| SEC-0 | DbReadNode 注入：提供绑定参数 + 禁止上游值拼接 SQL | Plugins/Standard |
| D-1 | 软删除全局过滤器 `HasQueryFilter` | Core/Data, Application |
| E-1 | 审计序列化失败不再静默丢事件 | Infrastructure/Audit |
| OBS-1 | 发布 `CredentialAccessedEvent`（凭据访问入审计） | Core/Events, Credential 解析路径 |
| OBS-2 | 发布缺失的执行事件（开始/节点完成/节点错误） | Runtime/Executor |
| CON-1 | 修复 ArrayPool 双重归还 | Host/WebSocket |

### P1 — 短期（稳定性/性能/安全加固）

| ID | 问题 | 影响模块 |
|----|------|---------|
| D-10 | 事务拆分：先提交 DB 再调外部调度 | Application/Workflows |
| D-11 | 执行列表查询投影瘦身 + `NodeRecords` 上限 | Application/Executions |
| CON-2 | 执行 Worker 并发化（先解 CON-3） | Host/Executor |
| CON-3 | 节点类型实例无状态化 | Runtime/Registry |
| SEC-1 | ShellToolNode 命令注入门禁 | Plugins/Standard |
| SEC-2 | 全局鉴权兜底 FallbackPolicy | Host |
| S-4 | Cookie 认证 CSRF 防护（SameSite=Strict/防伪造头） | Host/Auth |
| SEC-3 | Webhook 重放保护 + 限速 | Host/Webhooks |
| OBS-3 | DB 节点执行日志（脱敏 SQL/行数/耗时） | Plugins/Standard |
| DEP-1 | 清理提交 DLL + `.gitignore` 修正 + 轮换 dev 密钥 | plugins/, repo |
| TST-1 | IfNode 比较分支测试 | Tests/Runtime |
| TST-2 | 事务回滚测试 | Tests/Application |
| D-2/D-3/D-4 | 补触发器/存储文件索引 | Migrations |
| Q-3/Q-1 | 前端 Error Boundary + CSS Modules | Frontend |
| O-1/O-2 | Serilog 接收端 + OTel 基础追踪 | Host |
| EX-3 | 触发器调度补偿（提交后 Quartz 注册失败需补偿/告警，杜绝静默失效） | Application/Triggers |
| SEC-7 | JS 沙箱改白名单或 V8 Isolate 强隔离 | Core/Scripting |
| D-15 | 凭据唯一索引 NULL 跨库语义统一（sentinel 或条件化索引） | Core/Data |
| CON-6 | 调度空闲轮询改事件驱动唤醒 | Runtime/Executor |

### P2 — 中期（架构/可维护性）

| ID | 问题 |
|----|------|
| A-1/A-2 | 跨层接口抽象与位置统一 |
| A-4 | 拆分 `NodeExecutionContext` |
| DEP-2/DEP-3/DEP-4 | MediatR 移出 Core、统一 JSON、DB 抽象 |
| CQ-1 | 去除冗余 Application 服务接口 |
| EX-1/EX-2 | 引入 `DomainException` 基类；`NodeError` 去堆栈泄露 |
| EXT-1/EXT-2 | 前后端枚举单一来源；凭据/触发类型注册表化 |
| S-1/S-2/S-3 | 黑名单/锁定持久化；Webhook 密钥加密 |
| E-4 | 请求日志中间件 |
| D-5/D-6 | Schema 统一；列表投影 |
| CON-4/CON-5 | OAuth2 刷新去重；大批次内存限流 |
| T-2 | 关键查询 SQLite 集成测试 |
| D-14 | `SaveChangesAsync` 凭据用量增量维护，避免每次保存额外全量查询 |
| CQ-4 | 抽取 `SaveChangesInTransactionAsync()` 消除 TriggerService 3 处重复事务模板 |
| CQ-5 | 拆分 `WorkflowSchedulerKernel` 上帝类 |
| DEP-7 | `ServiceCollectionExtensions` 按模块拆分注册 |
| EXT-5 | Workflow JSON 列 schema 版本化/高频字段抽取 |
| TST-5 | `WorkflowService`/`WorkflowModificationService` 分支与端到端测试 |
| CON-7 | WebSocket 广播路径加锁/快照 |
| OBS-7 | WebSocket 推送结构化日志 + Meter |

### P3 — 长期优化

| ID | 问题 |
|----|------|
| D-7/D-8/D-9 | `NodeRecords` 拆表；多库迁移；迁移环境检查 |
| O-6 | 真实健康检查 + 指标 Meter |
| T-3/T-4/T-8 | PG 容器测试；并发测试；DTO 契约测试 |
| C-2/C-4 | HttpClient 池化；超时配置化 |
| Q-5/Q-7 | Props 命名统一；ScriptCache 竞态修复 |
| SEC-4/SEC-5/SEC-6 | 文件 ContentType 防护；DB 只读校验强化；密钥文件治理 |
| EXT-3/EXT-4 | NodeTypes DTO 映射；字段注册表化 |
| DEP-5/DEP-6 | 版本异常/预发布依赖治理 |
| EX-4 | Webhook 同步模式改事件通知，消除 DB 轮询 |
| D-16 | `WorkflowCredentialUsageBackfill` 改幂等分批/条件触发 |
| CQ-6/CQ-7 | i18n TODO 清理；反馈服务 NodeName 解析 |
| EXT-6 | 多脚本语言 `IScriptEngine` 注册表（若需） |

---

## 11. 附录：评分汇总

| 维度 | 初稿 | 修订 | 关键调整理由 |
|------|------|------|------|
| 架构与依赖治理 | 8.5 | 8.0 | 补 DLL 入库、MediatR 入 Core、双 JSON、并行 DB 栈、ServiceCollection 膨胀；mimo P2(DbContext 直注)与规范冲突、P3(WS 单例)已标注 |
| 代码质量与整洁度 | 7.5 | 7.3 | 补 14 冗余接口、DbRead 复制、前端硬编码 URL；**更正"无 TODO"误判**（47 处 i18n TODO）、TriggerService 事务重复、SchedulerKernel 887 行上帝类 |
| 数据层设计 | 7.0 | 6.8 | 补事务跨 await、列表大 JSON、分页、索引；D-1 共识；新增 SaveChanges 额外查询、凭据索引 NULL 跨库不一致、启动全量回填 |
| 并发内存性能 | 8.5 | 8.0 | 补 ArrayPool 双重归还、串行执行、可变单例、惊群、内存无界；新增 500ms 空闲轮询、WS 广播迭代（ScriptCache 锁已核正确） |
| 异常与容错 | 7.5 | 7.5 | 补无 DomainException 基类、NodeError 堆栈泄露；新增触发器调度补偿不完整、Webhook 同步轮询；62 catch 已核无吞没 |
| 安全审计 | 8.5 | 7.5 | **修正"无注入"误判**（SEC-0 Critical）、CSRF 升级、补 Shell 注入/全局鉴权/Webhook 重放/密钥泄露；新增 JS 沙箱黑名单可绕过（SEC-7）；mimo P3(ALC)/P4(WS 认证)误判已更正 |
| 可观测性 | 6.5 | 6.0 | **修正"审计覆盖"误判**（OBS-1 Critical、OBS-2 High）、DB 零日志、TraceId 不贯穿；校正健康检查为残缺桩；补 WS 推送日志不足 |
| 测试友好性 | 8.0 | 7.8 | 补 IfNode 分支/事务回滚/ScriptCache/插件加载测试缺口；新增 WorkflowService/ModificationService 大 Service 测试缺口 |
| 迭代扩展性 | 8.0 | 7.5 | 补前后端枚举漂移、Core 硬编码凭据/触发类型；新增 Workflow JSON 列 schema 演进、脚本仅 JS |
| **总体** | **8.2** | **7.1** | 三轮审计合计；安全/可观测性多项 High/Critical，数据层/代码质量补强；mimo 误判项已更正、规范冲突项已标注不适用 |

---

*报告初稿：DeepSeek（2026-07-24）*
*第一轮交叉核对与修订：9 维度独立子代理审计（2026-07-24）*
*第三轮并入：mimo 评审（2026-07-24），逐条源码复核并更正本方及 mimo 的不准确项*
*方法：全量源码静态分析 + 项目依赖图（ProjectReference）+ 安全审计清单 + 关键断言源码复核（SQL 参数、Cookie/CSRF、健康检查、审计事件发布、PluginLoader ALC、WebSocket 认证、TODO 计数、ScriptCache 锁范围）*

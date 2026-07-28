# 触发器（定时 / Webhook / 轮询）

> 本文档基于当前代码编写，以代码为准。触发器模型以 `FlowEngine.Core/Entities/Trigger.cs` 与 `TriggerSettings` 为权威来源；调度实现见 `FlowEngine.Host/Scheduling/QuartzScheduleManager.cs`、`Jobs/ScheduleTriggerJob.cs`、`Jobs/PollTriggerJob.cs`；Webhook 实现见 `FlowEngine.Host/Webhooks/WebhookRoutingMiddleware.cs` 与 `WebhookHandler.cs`。

触发器用于在满足时间、外部事件或外部状态变化时**自动启动工作流执行**，无需人工点击运行。Flow Engine 支持三类触发器：定时（Schedule）、Webhook、轮询（Poll）。对应 [系统总览](architecture/overview.md) 第 8 节。

## 1. 触发器模型

触发器实体 `Trigger` 关键字段：

- `WorkflowDefinitionId`：关联的工作流定义。
- `Type`：`TriggerType`（Schedule / Webhook / Poll）。
- `Name` / `IsActive`：名称与是否激活（停用则不触发）。
- `Settings`（`TriggerSettings` JSON 列）：随类型而异的配置，详见各节。
- `LastTriggeredAt` / `NextTriggerAt`：最近 / 下次触发时间，由引擎在触发后回写。

## 2. 定时触发器（Schedule，Quartz Cron）

基于 **Quartz.NET** 调度，由 `QuartzScheduleManager.RegisterScheduleAsync` 注册，`ScheduleTriggerJob` 执行。

- 配置字段（`TriggerSettings`）：`CronExpression`（标准 Quartz cron）、`TimeZone`（默认 UTC）、`StartAt` / `EndAt`（可选起止时间）。
- 每次到点，`ScheduleTriggerJob.Execute` 校验触发器存在且 `IsActive`，调用 `engine.StartAsync(workflowDefinitionId, triggerPayload: { triggerType: "Schedule", triggerId })` 启动执行，并回写 `LastTriggeredAt` / `NextTriggerAt`。
- 调度器生命周期由 `QuartzHostedService` 托管；重复注册会先删除旧 Job 再建新 Job。

cron 表达式语法见 Quartz 官方文档。时区用系统时区 ID（如 `Asia/Shanghai`）。

## 3. Webhook 触发器

Webhook 让外部系统通过 HTTP POST 主动触发工作流。运行时由 `WebhookRoutingMiddleware` 动态派发到 `WebhookHandler`，按路径实时查库路由（新增 / 删除路由无需重启）。

### 3.1 路由与触发

- 每个 Webhook 触发对应一条 `WebhookRoute`（含 `Path`、`TriggerId`、`WorkflowDefinitionId`、`IsSync`、`MaxWaitSeconds`）。
- 中间件仅对 `POST` 且非保留前缀（API / 健康检查 / WebSocket 等）的请求派发；命中路径后由 `WebhookHandler.HandleAsync` 按 `Path` 查路由并启动 `engine.StartAsync(..., triggerPayload: { triggerType: "Webhook", routePath, payload })`。
- 同步模式（`IsSync=true`）：最多等待 `MaxWaitSeconds`，事件驱动等待执行完成并返回状态；超时返回 `202 Accepted` 交由调用方后续查询。异步模式直接返回 `202` + `executionId`。

### 3.2 安全校验（`WebhookHandler.ValidateRequestAsync`）

| 校验 | 行为 |
|------|------|
| 匿名防护（H3） | 若路由**既无 `Secret` 也无 `AllowedIps`**，直接拒绝（`401`），防止任意匿名 POST 触发 |
| 签名验证 | 配置 `Secret` 时，要求 `X-Hub-Signature-256` 头；以 HMAC-SHA256 对 `timestamp.nonce.body` 签名，**恒定时间比较**，失败返回 `401` |
| 防重放（SEC-3） | 启用重放保护或配置密钥时，要求 `X-Webhook-Timestamp` + `X-Webhook-Nonce`；在启用重放保护时校验时间戳新鲜度与 nonce 唯一性 |
| 限流 | 启用限流时按「路由路径 + 客户端 IP」限流，超限返回 `429` |
| IP 白名单 | 配置 `AllowedIps` 时，客户端 IP 不在列表返回 `403` |
| 来源域白名单 | 配置 `AllowedOrigins` 时，要求 `Origin` 头且需在列表内，否则 `403` |
| 幂等 | 可配置 `IdempotencyKeyTemplate`（支持 `{headers.x}` / `{body.field}`），重复键命中返回既有 `executionId` |

> 对应 [系统总览](architecture/overview.md) 第 9 节：Webhook 入口支持签名验证或来源白名单。

## 4. 轮询触发器（Poll）

轮询触发器由 `QuartzScheduleManager.RegisterPollTriggerAsync` 以固定间隔（`SimpleScheduleBuilder`，`IntervalSeconds`）周期性执行 `PollTriggerJob`。

- 配置字段：`IntervalSeconds`（轮询间隔，默认 60）、`TimeoutSeconds`、`PollNodeId`（用于拉取外部数据的节点类型 ID）、`DedupStrategy`（去重策略 `None` / `Id` / `Timestamp` / `HashSet`）、`SkipIfRunning`（上一次仍在运行则跳过，默认 `true`）、`IdempotencyTtlSeconds` 等。
- 每次执行：`PollTriggerJob` 加载 `PollNodeId` 对应节点类型，构造节点执行上下文并**执行该节点以拉取外部数据**；对返回的数据项按去重策略过滤。
- 仅对「新数据项」调用 `engine.StartAsync(workflowDefinitionId, triggerPayload: { triggerType: "Poll", triggerId, data })`，并基于 SHA256 计算幂等键（`poll-exec:{triggerId}:{hash}`）做缓存 + 数据库双层幂等兜底，避免重复触发。
- 去重游标仅基于**成功触发**的项推进，失败项下一轮重试（避免被永久跳过）。

## 5. 配置触发器的位置

- **在 UI 中配置**：触发器（含三类）的创建 / 编辑 / 启停通常在工作流编辑器的触发器面板完成（具体菜单与表单字段以 UI 实际为准，本文不逐项核对前端文案）。
- 也可用 API / MCP 技能间接操作（Webhook 路由需配套 `WebhookRoute` 记录，由后端在配置 Webhook 触发器时建立）。

## 6. 备注

- 各类触发器的具体 UI 配置步骤（字段名、表单路径）以前端实际为准，本文未逐一核对。
- Webhook 鉴权为 **HMAC 签名**（`X-Hub-Signature-256`，基于路由 `Secret`）+ 可选**按路由 `AllowedIps` 白名单**，并内置重放缓存（`WebhookReplayCache`）与全局限流（`RateLimiting`）；相关配置位于 `appsettings.json` 的 `Webhook` 与 `RateLimiting` 节点，无独立的 `WebhookSecurityOptions` 开关。
- 节点内如需读取触发上下文，以执行引擎注入的全局变量（见 [表达式](concepts/expressions.md)）与节点运行上下文为准。

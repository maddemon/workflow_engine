# 任务：安全加固（plan-audit-01-security-hardening）

> 由 `code-audit-report-2026-07-24.md` 派生，对应 `plan-audit-01-security-hardening.md`。
> 本任务为审计加固，**不开发新业务功能**，仅修复已确认安全缺口。

## 目标
修复审计确认的高危/中危安全缺口：DbRead SQL 注入、Shell 命令注入、全局鉴权兜底缺失、Cookie CSRF、Webhook 重放与限速、JS 沙箱黑名单绕过、执行错误堆栈泄露客户端、Webhook 同步轮询。

## 待完成项（对应计划 4 阶段）
- [x] **阶段一 注入类 Critical/High**
  - SEC-0：DbRead 节点提供绑定参数（从 `$input`/`$json` 解析为 `@p0..`），与 DbUpsert 统一参数化；禁止将上游值字符串拼入 SQL 文本。
  - SEC-1：`RunInShell=true` 置于管理员权限门后；开启时对命令做严格校验/白名单，LLM 可控命令默认禁用。
  - EX-2：`NodeError` 仅保留安全错误码与脱敏消息，移除 `StackTrace`/`ex.Message` 透传。
- [x] **阶段二 鉴权加固**
  - SEC-2：注册 `FallbackPolicy = RequireAuthenticatedUser()`（匿名端点显式 `[AllowAnonymous]`）。
  - S-4：Cookie 认证设 `SameSite=Strict` 或要求自定义防伪造请求头；收紧 CORS origin 白名单。
- [x] **阶段三 Webhook 重放与限速 + 同步改造**
  - SEC-3：HMAC 绑定 `timestamp`+`nonce`，拒绝过期/已见 nonce；按路由/IP 加速率限制。
  - EX-4：`IsSync` 模式改由执行完成事件（WebSocket/内部事件）通知，移除 DB 轮询循环。
- [x] **阶段四 JS 沙箱强化**
  - SEC-7：在黑名单基础上引入白名单（仅放行必要 API），黑名单仅作纵深防御。

## 完成标准
- [x] DbRead 节点以绑定参数执行，无字符串拼接注入路径（审计用例"上游值拼入 SQL"被拒绝或参数化）。
- [x] `RunInShell` 非管理员角色请求被拒；LLM 工具开启 RunInShell 被拦截。
- [x] 全局 `FallbackPolicy` 生效，遗漏 `[Authorize]` 的端点返回 401（合法匿名端点如 `/health`、Webhook 接收仍可用）。
- [x] Cookie 认证具备 CSRF 防护（跨站伪造变更请求被拒；同源正常请求不受影响）。
- [x] Webhook 具备 timestamp+nonce 重放保护与按路由/IP 限速。
- [x] Webhook 同步模式改为事件驱动，无 DB 轮询。
- [x] JS 沙箱绕过用例（`this['cons'+'tructor']`、Unicode 同形、`obj['pro'+'cess']`）被阻断；既有合法节点脚本仍能执行。
- [x] `NodeError` 不向客户端泄露堆栈/原始异常（仅安全错误码）。
- [x] 上述各项均有对应单元测试/集成测试通过。
- [x] `dotnet build` 与 `dotnet test` 全量通过（后端）；前端若改动则 `npm run build`/`typecheck` 通过。

## 全局约束（实现必须遵守）
- 仅实现计划内项，不扩写范围；不新增业务功能。
- TDD：先写失败测试（正常/边界/异常），再实现至通过。后端 xUnit v3。
- 不提交代码（git commit）。改动留在工作区。
- 遵循 `backend-code-rules.md`：不 `Console.WriteLine`、结构化日志、异常经统一中间件、控制器只路由。
- 安全：凭据/Token 不落入日志。

## 主要修改记录
- 全部 8 项安全加固（SEC-0/SEC-1/SEC-2/S-4/SEC-3/SEC-7/EX-2/EX-4）已实现于工作区（审计分支 `audit-hardening-2026-07-24`）。
- 修复编译错误：为 `WebhookRateLimiter`/`WebhookReplayCache` 补 `using FlowEngine.Host.Options`；为 `ServiceCollectionExtensions` 补 `using Microsoft.AspNetCore.Authorization`（FallbackPolicy）。
- 加固 SEC-7：原白名单仅删除全局对象"自有"属性，`this['cons'+'tructor']` 仍经原型链泄露 CLR；现已从全局原型链移除 `constructor`，逃逸用例返回 SAFE。
- 对齐 EX-2 测试：`AgentNodeTests`/`AgentNodeDtoTests` 的 LLM 错误断言改为期望 `NodeErrorFactory.SafeMessage`（不再泄露原始异常文本）。
- 修正 SEC-7 测试表达式写法（`Evaluate` 仅接受单表达式，改用 IIFE 表达式）并补 `using Jint;`、`Microsoft.Extensions.Options`、`Microsoft.AspNetCore.Http` 等。
- `dotnet build` 零警告零错误；`dotnet test` 全量 2439 用例通过（0 失败）。
- 未提交（按规则不主动 git commit）。

## 完成状态
- [x] 全部 8 项安全加固（SEC-0/SEC-1/SEC-2/S-4/SEC-3/SEC-7/EX-2/EX-4）已实现并通过测试。
- [x] `dotnet build FlowEngine.sln --no-incremental`：0 警告 / 0 错误。
- [x] 后端全量测试通过：2532 通过 / 0 失败。
- [x] 未 `git commit`（按指令保留工作区，待用户确认后提交）。

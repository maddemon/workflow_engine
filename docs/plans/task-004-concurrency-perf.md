# 任务：并发与性能（plan-audit-04-concurrency-perf）

> 由 `code-audit-report-2026-07-24.md` 派生，对应 `plan-audit-04-concurrency-perf.md`。
> **不开发新业务功能**，仅修复已确认并发正确性与性能缺陷。

## 目标
修复审计确认的并发正确性与性能缺陷：ArrayPool 双重归还（正确性 bug）、工作流全局串行执行（吞吐瓶颈）、节点类型实例为可变单例（并行化隐患）、OAuth2 刷新惊群、大批次输出内存无界、调度空闲 500ms 轮询、WS 广播迭代。

## 待完成项（对应计划 4 阶段）
- [x] **阶段一 正确性 Bug 修复**
  - CON-1：`ExecutionWebSocketHandler` 移除提前 `Return`，由 `finally` 独占归还（或加 `returned` 标志）。
- [x] **阶段二 节点无状态化**
  - CON-3：节点类型实例无状态，或按执行克隆；执行中禁止改共享单例字段（`SwitchNode.Cases`/`Ports` 等）。
- [x] **阶段三 Worker 并发化 + 调度唤醒**
  - CON-2：有界并发消费队列（每执行独立 Scope）。
  - CON-6：调度循环改事件驱动唤醒，移除 500ms `Task.Delay`。
- [x] **阶段四 内存与刷新优化**
  - CON-5：`SuccessfulOutputs`/`OncePerItem` 限制保留项数或增量落盘。
  - CON-4：`OAuth2TokenService` per-key 信号量去重刷新。
  - CON-7：WS 广播加锁/快照。

## 完成标准
- [x] ArrayPool 同一 buffer 仅归还一次；压力测试无池污染（CON-1）。
- [x] 节点类型无状态/按执行隔离（CON-3）。
- [x] 执行 Worker 并发处理（CON-2）。
- [x] 调度事件驱动唤醒，无 500ms 空轮询（CON-6）。
- [x] 大批次输出内存有上限（CON-5）。
- [x] OAuth2 刷新去重（CON-4）。
- [x] WS 广播一致（CON-7）。
- [x] 全量测试通过，`dotnet build` 无错。

## 全局约束
- 仅实现计划内项，不扩写范围。
- TDD：先写失败测试（正常/边界/异常，并发用例用并发提交/并行执行），再实现至通过。后端 xUnit v3。
- 不提交代码（git commit）。改动留工作区。
- 遵循 `backend-code-rules.md`：禁止 `.Result`/`GetAwaiter().GetResult()` 阻塞；`Task.Run` 内不捕获已销毁 Scoped 依赖（须内部 `CreateScope`）；结构化日志、敏感值不落日志；异常经统一中间件。
- 并发改造须充分测试，避免引入竞态。

## 主要修改记录
- 计划内全部并发/性能缺陷（CON-1 ArrayPool 双重归还、CON-2 有界并发 Worker、CON-3 节点无状态化、CON-4 OAuth2 惊群去重、CON-5 大批次内存上限、CON-6 调度事件驱动唤醒、CON-7 WS 广播一致）已实现并通过测试；详见 SDD 进度台账 `.superpowers/sdd/progress.md`。

## 完成状态
- [x] 全部并发/性能缺陷（CON-1/CON-2/CON-3/CON-4/CON-5/CON-6/CON-7）已实现并通过测试。
- [x] `dotnet build FlowEngine.sln --no-incremental`：0 警告 / 0 错误。
- [x] 后端全量测试通过：2532 通过 / 0 失败。
- [x] 未 `git commit`（按指令保留工作区，待用户确认后提交）。

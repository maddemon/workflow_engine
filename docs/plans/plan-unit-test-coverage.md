# 单元测试覆盖率提升计划（70%+ 目标）

执行方式：使用 `subagent-driven-development` 逐任务执行。每个任务遵循"先写失败测试 → 补全/修正测试 → 全绿"的节奏。所有类名与方法签名均已对照源码核实（见各 `task-00X-*.md`），本计划只描述目标、阶段与验收，不粘贴完整测试实现。

## 1. 概述

- 目标：后端综合行覆盖率 68.9% → **75%+**（用户 2026-07-17 决策：冲更高标准）；前端 16.43% 行 → 65%+。
- 范围：补全后端 Infrastructure / Host / Core / Plugins.Standard 与前端 service / store / utils / 页面的单元测试。
- 不覆盖：纯 getter/setter 实体仅做最小属性往返断言（低成本抬升行覆盖，不追求语义深度）。
- 重要前提：原草稿中大量类名与方法签名为臆测，已逐项对照源码修正。每个 Task 的真实 API 见对应 `task-00X-*.md`。
- 命名说明：本计划为跨模块/跨阶段的质量提升计划，不隶属单一交付阶段，故沿用与 `plan-runtime-stability.md` / `plan-cleanup-01-*` 一致的跨阶段命名 `plan-unit-test-coverage.md`，而非 `plan-{stage}-NN-` 形式（符合 docs-rules 对特殊计划的既有实践）。

### 1.1 实测基线（Task 008 第一步产出，2026-07-17）

后端按行数加权综合 **68.9%**（覆盖行 17464 / 总行 25329，跨 7 个测试项目取各程序集最优）。原草稿的"54% 后端 / 50.4% 前端"为臆测数字，已以下方实测值替换：

| 程序集 | 基线 line-rate | 覆盖行/总行 | 与原目标关系 |
|--------|---------------|-------------|--------------|
| Core | 52.5% | 2137 / 4070 | 距 55% 目标差 ~2.5pt |
| Application | 76.8% | 3657 / 4764 | **已超 60% 目标**，仅补关键路径缺口 |
| Runtime | 65.0% | 2051 / 3153 | **已超 60% 目标**，仅补关键路径缺口 |
| Infrastructure | 41.7% | 365 / 875 | 距 50% 目标差 ~8.3pt（真实缺口） |
| Host | 58.9% | 2043 / 3469 | 距 70% 目标差 ~11pt（真实缺口） |
| Migrations | 97.0% | 4946 / 5100 | 非目标模块，已达 |
| Resources | 57.5% | 73 / 127 | 真实缺口，按需补 |
| Plugins.Standard | 58.1% | 2185 / 3761 | 真实缺口，按需补 |
| TestPlugin | 70.0% | 7 / 10 | 测试工程，非目标 |

前端（v8 provider，`npx vitest run --coverage`，19 文件 / 118 用例全绿）：**语句 15.29% / 分支 13.97% / 函数 9.9% / 行 16.43%**。原草稿"50.4%"为臆测，实际仅 16.43%，是本次剩余工作中占比最大的部分。低覆盖重灾区：services/api.ts 2.58%、stores/workflowStore.ts 21.85%、pages（多数 0%）、ParameterPanel/fields 全 0%。

**结论（呼应评审点 #8 + 用户 2026-07-17 决策）**：原设后端整体目标 70%，实测 68.9% 已近线；但用户决策改为冲 **75%+** 更高标准，故 Application/Runtime 仍需继续拉升（不再"仅补强"），并要求 **Infrastructure、Host、Core、Plugins.Standard** 实质性补测。前端（16.43%→65%）依旧是最大单一缺口。§3 目标表已据此重新校准，避免为凑数而注水（getter/setter 往返仅作低成本补充，不计入高价值覆盖）。

## 2. 交付物清单

- 各模块新增 `*Tests.cs` / `*.test.ts`（路径与类名见各 task 文档）。
- 唯一允许的非逻辑改动：必要时在 `FlowEngine.Runtime` 程序集加 `[InternalsVisibleTo("FlowEngine.Runtime.Tests")]`，以便测试 internal 类型（converters、`CodeParameterExtractor`）。
- 前端 `frontend/vite.config.ts` 已补 `test.coverage` 配置（`provider: "v8"`），使 `npx vitest run --coverage` 可用。
- 8 个任务文档：`docs/plans/task-00X-*.md`。
- 最终覆盖率报告（Task 008 产出）。

## 3. 开发阶段（按模块拆分）

| Phase | 模块 | 任务文档 | 基线 → 目标（75%+ 标准） | 投入评估 |
|--------|------|-----------|----------------|----------|
| 1 | 后端 Application | `task-001-backend-application.md` | 76.8% → 82%+ | 真实缺口 ~5pt，中投入（鉴权/校验/映射深补） |
| 2 | 后端 Core | `task-002-backend-core.md` | 52.5% → 65%+ | 真实缺口 ~12.5pt，中高投入（实体/值对象/枚举） |
| 3 | 后端 Runtime | `task-003-backend-runtime.md` | 65.0% → 75%+ | 真实缺口 ~10pt，中高投入（错误策略/水合/表达式） |
| 3b | 后端 Plugins.Standard | `task-003-backend-runtime.md`（同测试工程） | 58.1% → 70%+ | 真实缺口 ~12pt，中高投入（节点逻辑） |
| 4 | 后端 Infrastructure | `task-004-backend-infrastructure.md` | 41.7% → 65%+ | 真实缺口 ~23pt，高投入（存储/身份/Token） |
| 5 | 后端 Host 控制器 | `task-005-backend-host-controllers.md` | 58.9% → 75%+ | 真实缺口 ~16pt，高投入 |
| 6 | 后端 Host 中间件 | `task-006-backend-host-middlewares.md` | （并入 Host 目标） | 随 Phase 5 |
| 7 | 前端 | `task-007-frontend.md` | 16.43% 行 → 65%+ | **最大缺口**（差 ~49pt），高投入 |
| 8 | 验证与收尾 | `task-008-verification.md` | 后端 75%+ / 前端 65%+ | 实测回填 + 回流 |

各 Phase 内部任务可并行派发子 agent。Phase 5/6（Host）依赖 Application/Runtime/Infrastructure 程序集，建议在其后执行；前端（Phase 7）独立且工作量最大，可同步启动。

> 校准依据：上表"基线"列全部取自 Task 008 实测（2026-07-17），原草稿的 35.6% / 31.9% / 42.8% / 26.2% / 50.4% 为臆测值，已作废。用户 2026-07-17 决策将后端整体目标由 70% 上调至 **75%+**，故各模块目标同步抬高（Application/Runtime 不再"仅补强"，改为实质性拉升），以避免冲 75% 时整体不达标。注水式 getter/setter 测试仅作低成本补充，不计入高价值覆盖。

## 4. 阶段依赖图

```text
Phase1(Application) ─┐
Phase2(Core)        ─┤
Phase3(Runtime)     ─┼─> Phase5(Host 控制器) ─┐
Phase4(Infrastructure)┘                          Phase6(Host 中间件) ─> Phase8(验证)
Phase7(前端) ─────────────────────────────────────────────┘
```

## 5. 风险与待定项

- 基线数字已于 Task 008 实测并回填 §1.1（后端加权 68.9% / 前端 16.43% 行）。原草稿的 54% / 50.4% 等臆测值已作废。
- internal 类型（converters、`CodeParameterExtractor`）需经公共入口 `ParameterHydrator.HydrateAsync` 测试，或加 `[InternalsVisibleTo]`。
- 实体 getter/setter 测试属"覆盖率注水"，仅作低成本补充，不计入高价值覆盖。
- 不修改生产逻辑；如确需 `[InternalsVisibleTo]` 仅限 Runtime 一处。
- `ExecutionCleanupService` 已由现有 `ExecutionCleanupServiceTests.cs` 覆盖其唯一公共方法 `CleanupAsync`，**不重复劳动**，不计入剩余待补工作量。
- 后端整体已近 70%（68.9%），Phase 1/3（Application/Runtime）以降低投入原则执行，避免为凑数而注水；真正缺口在 Infrastructure / Host / Core / Plugins.Standard 与整个前端（见 §1.1）。

## 6. 验收总标准

- 每个 Task：对应测试项目 `dotnet test` / `npx vitest run` 全绿；**且 `dotnet build` / 前端 `npm run build` + `npm run typecheck` 无编译/类型错误**（新增测试不得引入签名或 TS 类型错误）。
- 覆盖率口径统一为 **Cobertura line-rate**（行覆盖率）；**branch-rate 仅作参考，不计入达标判定**。前端同口径取 v8 报告的 `% Lines`。
- 后端整体行覆盖率 ≥ 75%（实测基线 68.9%，按新标准缺口 ~6pt，需 Core/Infrastructure/Host/Plugins/Runtime/Application 共同拉升）；前端 ≥ 65%（实测基线 16.43%，缺口大，为主要工作量）。以 Task 008 实测为准，未达标则回流对应 Phase 补测。
- 强制约束：
  - 后端除 `FlowEngine.Host.Tests` 外**禁止使用 Moq**；全仓库**禁止使用 FluentAssertions**，统一用 xUnit `Assert.*`。
  - 前端**禁止 `as any`**（违反 `frontend-code-rules §7`）；render 页面须按真实 provider 装配（AuthProvider / Router / i18n / Mantine）。
  - 不得调用不存在的 API（所有签名以 task 文档核实结果为准）。

# 测试（Testing）

> 本文档基于当前代码编写，以代码为准。后端采用 **xUnit v3**，前端采用 **Vitest**。

## 1. 后端测试（xUnit v3）

### 1.1 运行全部测试

仓库根目录执行：

```bash
dotnet test
```

`dotnet test` 会扫描解决方案下所有测试项目并运行。

### 1.2 测试项目位置

测试工程位于 `tests/`（与 `backend/` 平级），已确认存在：

| 测试项目 | 覆盖层级 |
|----------|----------|
| `tests/FlowEngine.Core.Tests`        | 实体、契约、表达式/脚本、值对象等核心逻辑 |
| `tests/FlowEngine.Application.Tests` | 用例编排、Service、DTO 转换 |
| `tests/FlowEngine.Runtime.Tests`     | 执行引擎、插件节点（含 `Plugins/` 下节点行为） |
| `tests/FlowEngine.Host.Tests`        | 端到端：`WebApplicationFactory` 起 Host 做 HTTP 集成测试 |
| `tests/FlowEngine.Infrastructure.Tests` | 持久化/调度/加密等适配器 |

> 可按项目过滤：`dotnet test tests/FlowEngine.Runtime.Tests`；按用例过滤：`dotnet test --filter "ExpressionEvaluatorComparisonTests"`。

### 1.3 TDD 约定（先写失败测试）

项目硬性要求 **先写测试再实现**（后端代码规范第 12 条 / 项目规则第 6 节）：

```
1. 写一个会失败的测试（非法输入 / 异常复现 / 边界）
2. 运行测试确认失败
3. 编写最小实现让测试通过
4. 运行测试确认通过
5. 重构并跑全量测试确认无回归
```

节点插件须覆盖：正常执行、空/缺参错误、`JsonElement` 类型转换、输出符合 `DataBatch`→`DataItem`。命名形如 `Resolve_JsonElement_String_Evaluates_Expression`。

### 1.4 测试要点

- 单元测试偏重表达式/参数解析/DTO/业务规则；集成测试用 `WebApplicationFactory` 端到端。
- 必须覆盖：正常路径、边界（空值/空串/空集合/零值）、类型转换、异常路径。
- 禁止用 `Console.WriteLine` 输出；断言失败信息由测试框架承载。

## 2. 前端测试（Vitest）

### 2.1 运行测试

```bash
cd frontend
npm test        # = vitest run，单次跑完
npm run test:watch  # 监听模式
```

测试配置在 `frontend/vite.config.ts`（`vitest/config` 的 `test` 段）：

```ts
test: {
  environment: "jsdom",
  globals: true,
  setupFiles: ["./src/test-setup.ts"],
  coverage: {
    provider: "v8",
    reportsDirectory: "./coverage",
    include: ["src/**/*.{ts,tsx}"],
    thresholds: { lines: 65, branches: 50 },
  },
},
```

### 2.2 覆盖率门禁

`vite.config.ts` 的 `coverage.thresholds` 当前设定：

| 指标 | 门禁 |
|------|------|
| 行覆盖率 `lines` | 65% |
| 分支覆盖率 `branches` | 50% |

`npm test` 在覆盖率跌破门禁时会失败，需先补齐用例再提交。

### 2.3 测试策略

| 层级 | 覆盖目标 | 工具 |
|------|----------|------|
| 单元测试 | 工具函数、`validateParameters`、`computeDynamicPorts`、序列化 | Vitest |
| 组件测试 | 渲染、用户交互 | Vitest + React Testing Library |

命名规范：`{函数名/组件名} - {场景} - {预期结果}`，如 `validateParameters - required field empty - returns error`。

## 3. 提交前检查

- 后端：`dotnet build` + `dotnet test` 全绿。
- 前端：`npm run build` + `npm run typecheck` + `npm test` 全绿。
- Code Review 前必须完成上述编译与测试（项目规则第 5 节）。

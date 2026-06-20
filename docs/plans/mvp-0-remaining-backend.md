# MVP-0 后端未完成工作

> 更新时间：2026-06-20
> 状态：进行中

---

## 1. 认证授权（高优先级）

### 问题

所有 API 端点无认证，凭据接口完全开放。

### 涉及文件

- `backend/FlowEngine.Host/Program.cs` — 无 `AddAuthentication`
- `backend/FlowEngine.Host/Controllers/*.cs` — 无 `[Authorize]`

### 实现方案

1. 添加 JWT Bearer 认证中间件
2. Controller 添加 `[Authorize]` 特性
3. 前端 axios 拦截器添加 Token

### 验收标准

- [ ] 未登录时 API 返回 401
- [ ] Token 过期时返回 403
- [ ] 前端自动刷新 Token

---

## 2. CORS 配置（高优先级）

### 问题

`Program.cs:66-71`：未配置 `Cors:AllowedOrigins` 时 AllowAnyOrigin。

### 实现方案

在 `appsettings.json` 中配置允许的来源，未配置时拒绝而非允许所有。

---

## 3. 多输入等待机制（中优先级）

### 问题

WhenArea 等待机制已实现，但需要验证多输入节点（如 Merge）的正确行为。

### 涉及文件

- `backend/FlowEngine.Runtime/WaitingArea/WaitingArea.cs`
- `backend/FlowEngine.Runtime/Executor/WorkflowExecutor.cs:365-419` — RouteOutputsAsync

### 验收标准

- [ ] 2 个输入端口的节点等待所有输入到达后才执行
- [ ] 超时后按错误策略处理

---

## 4. 错误重试验证（中优先级）

### 问题

重试策略代码已实现，需要端到端验证。

### 涉及文件

- `backend/FlowEngine.Runtime/Executor/WorkflowExecutor.cs:325-363` — ExecuteNodeWithRetryAsync
- `backend/FlowEngine.Core\Entities\RetryPolicy.cs` — BaseDelay 未被使用（H5）

### 验收标准

- [ ] 配置 maxRetries=2 的节点失败后重试 2 次
- [ ] 重试间隔按指数退避计算
- [ ] BaseDelay 被正确使用

---

## 5. 执行取消（中优先级）

### 问题

`CancellationToken` 已传递但未验证取消行为。

### 验收标准

- [ ] 调用取消 API 后执行停止
- [ ] 已完成的节点记录保留
- [ ] 未执行的节点跳过

---

## 6. 单元测试覆盖率 ≥ 30%（中优先级）

### 当前状态

- Core Tests: 15 通过
- Runtime Tests: 65 通过
- Application Tests: 5 通过
- **总计: 85 通过**

### 待补充

| 模块 | 当前覆盖 | 目标 |
|------|----------|------|
| ExpressionEvaluator | 部分 | 补充函数调用、三元表达式测试 |
| ParameterResolver | 部分 | 补充嵌套字典/列表测试 |
| WorkflowExecutor | 部分 | 补充多输入等待、错误重试测试 |
| WorkflowValidator | 无 | 新增验证逻辑测试 |

---

## 7. 性能基线 ≥ 10 TPS（低优先级）

### 实现方案

1. 编写性能测试脚本
2. 测量线性工作流（3 节点）的吞吐量
3. 记录基线数据

---

## 优先级排序

| 优先级 | 工作项 | 预估工时 |
|--------|--------|----------|
| P0 | 认证授权 | 2 天 |
| P0 | CORS 配置 | 0.5 天 |
| P1 | 多输入等待验证 | 1 天 |
| P1 | 错误重试验证 | 1 天 |
| P1 | 执行取消 | 1 天 |
| P1 | 测试覆盖率补充 | 2 天 |
| P2 | 性能基线 | 1 天 |

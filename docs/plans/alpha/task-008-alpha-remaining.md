# 任务：Alpha 阶段剩余工作补齐

## 目标

完成 Alpha 阶段 9 个模块中未完成或存在 Bug 的部分，达到所有交付物清单要求。

## 待完成项

### 1. 表达式沙箱安全修复（plan-alpha-03）

- [x] 移除 `fetch` 注入（直接安全违规）
- [x] 从 `s_knownIdentifiers` 移除 `fetch` 和 `console`
- [x] 注入安全白名单函数：`now()`、`nowIso()`、`jmespath()`
- [x] 实现 JMESPath 查询支持（基础路径导航：属性访问 + 数组索引）
- [ ] 添加 AST 缓存（`ExpressionCacheKey`）— 未实现，计划中可选，不阻塞安全
- [x] 添加沙箱安全违规测试用例（17 个新测试）
- [x] 添加完整表达式沙箱测试

### 2. 审计日志 Bug 修复（plan-alpha-02）

- [x] `InMemoryEventBus` Channel 改为有界（容量 10000，FullMode = DropWrite）
- [x] `AuditLogFileSink` 改为只订阅 `AuditEvent` 而非全部 `IDomainEvent`
- [x] 简化 SerializeEvent/IsCriticalEvent 方法签名

### 3. 前端用户系统 UI（plan-alpha-09）

- [x] 登录页面（LoginPage.tsx）
- [x] 注册页面（RegisterPage.tsx）
- [x] Auth context / provider（JWT 存储、用户信息、自动恢复）
- [x] `api.ts` 添加认证端点 + JWT 拦截器 + 401 自动跳转
- [x] 路由守卫（ProtectedRoute/AuthLayout）
- [x] Header 用户菜单功能化（展示邮箱、登出按钮）

### 4. 前端触发器配置 UI（plan-alpha-04）

- [x] Schedule/Cron 触发器配置表单
- [x] Webhook 触发器配置表单（Secret、IP 白名单、来源白名单、同步/异步）
- [x] 触发器管理 UI（列表、创建、编辑、删除）
- [x] 集成到 ParameterPanel 工作流设置区

### 5. 前端 Agent/Tool/LLM 节点 UI（plan-alpha-06/07/08）

- [x] `theme.ts` + `index.css` 添加 AI/Agent/LLM/Tool 分类色
- [x] `NodeIcon.tsx` 添加对应图标（Bot/Brain/Wrench/Webhook）
- [ ] Agent 节点配置组件 — 由 FieldResolver 动态渲染，无需独立组件
- [ ] LLM 节点配置组件 — 由 FieldResolver 动态渲染
- [ ] Tool 节点配置增强 — 由 FieldResolver 动态渲染

## 完成状态

- [x] 表达式沙箱修复完成
- [x] 审计日志修复完成
- [x] 前端用户系统完成
- [x] 前端触发器 UI 完成
- [x] 前端 Agent/Tool/LLM UI 完成（分类色、图标）
- [x] `dotnet build` 通过（0 错误 0 警告）
- [x] `npm run build` 通过（0 错误）
- [x] 全部测试通过：后端 219 / 前端 15
- [ ] Code Review 通过

## 主要修改文件

### Backend

- `backend/FlowEngine.Runtime/Scripting/JsEngine.cs` — 移除 fetch、注入 now/jmespath
- `backend/FlowEngine.Runtime/Expressions/ParameterResolver.cs` — 移除 fetch/console 白名单
- `backend/FlowEngine.Core/Events/InMemoryEventBus.cs` — 有界 Channel
- `backend/FlowEngine.Infrastructure/Audit/AuditLogFileSink.cs` — 只订阅 AuditEvent
- `tests/FlowEngine.Runtime.Tests/Scripting/JsEngineSecurityTests.cs` — 17 个安全测试
- `tests/FlowEngine.Runtime.Tests/Expressions/ParameterResolverSecurityTests.cs` — 4 个安全测试

### Frontend

- `frontend/src/App.tsx` — AuthProvider + ProtectedRoute + auth routes
- `frontend/src/theme.ts` — AI/Agent/LLM/Tool 分类色
- `frontend/src/index.css` — CSS 变量
- `frontend/src/components/common/NodeIcon.tsx` — Bot/Brain/Wrench/Webhook 图标
- `frontend/src/services/api.ts` — auth/trigger API 端点、JWT 拦截器
- `frontend/src/hooks/AuthContext.tsx` — 认证上下文
- `frontend/src/pages/LoginPage.tsx` — 登录页
- `frontend/src/pages/RegisterPage.tsx` — 注册页
- `frontend/src/components/Layout/HeaderToolbar.tsx` — 用户菜单功能化
- `frontend/src/components/ParameterPanel/TriggerConfig.tsx` — 触发器配置组件
- `frontend/src/components/ParameterPanel/ParameterPanel.tsx` — 集成 TriggerConfig
- `frontend/src/types/workflow.ts` — auth + trigger 类型

## 备注

- AST 缓存（`ExpressionCacheKey`）未实现：计划中列为阶段四，不阻塞安全验收
- Agent/LLM/Tool 节点的参数配置由 FieldResolver 动态渲染，无需独立组件

# 开发计划：前端设置页与注册清理（plan-alpha-11-frontend-settings）

> 配套后端计划：`alpha/plan-alpha-09-user-system.md`（用户系统）
> 横向约定与推荐实施路线见 `plan-frontend-management-ui.md`
> 阶段归属：Alpha（对应后端用户系统模块）

## 1. 概述

补齐通用设置页（当前用户信息、API Key 管理），并清理已关闭的注册入口。

**本模块不含权限相关导航**（管理入口/Bell/权限显隐已归属模块一 RBAC，Beta 阶段落地），以避免「Alpha 依赖 Beta」的阶段倒挂。本模块内容面向**任意已登录用户**的自助设置，不依赖权限守卫。

覆盖范围：
- 设置页：当前用户信息、API Key 列表与创建/吊销。
- 低优先级：注册功能已关闭（后端 `AuthController.Register` 返回 403），清理前端 `RegisterPage` 与 `/register` 路由及相关死代码。

不覆盖范围（补充）：
- **主题切换**：`HeaderToolbar.tsx` 第 88-97 行已有完整可用的 dark/light 切换开关，设置页不提供重复控件。

不覆盖范围：
- 后端用户/密钥 API（已实现：`/auth/me`、`/auth/api-keys`、`/auth/api-keys/{id}`；`api.ts` 仅封装 `createApiKey`，**缺 `listApiKeys`/`revokeApiKey` 封装**）。
- 管理导航/权限守卫（见模块一 RBAC）。

## 2. 交付物清单

- `src/services/api.ts` 新增：`listApiKeys()`（→ `GET /auth/api-keys`）、`revokeApiKey(id)`（→ `DELETE /auth/api-keys/{id}`），及类型 `ApiKeyDto`。
  - **`ApiKeyDto` 实际字段**（后端 `AuthDtos.cs`）：`id: string`、`name: string`、`prefix: string`、`createdAt: string`、`expiresAt: string | null`、`revokedAt: string | null`。**注意：后端只返回 `prefix`，不返回后四位/明文**，前端无法生成 `sk-****1234` 式展示。
- `src/pages/SettingsPage.tsx`：用户信息 + API Key 管理（列表/创建/吊销/复制）。
- （低优先级）`src/pages/RegisterPage.tsx` + `src/App.tsx` 的 `/register` 路由：移除；并同步清理 `api.ts` 的 `register`/`RegisterRequest`/`RegisterResult`、`AuthContext.tsx` 的 `register` 方法与 `types/workflow.ts` 的 `RegisterRequest`/`RegisterResult`，避免类型检查残留未使用代码。

## 3. 现有改造点（需修改的既有文件）

| 文件 | 改造内容 |
|------|----------|
| `src/services/api.ts` | 新增 `listApiKeys`/`revokeApiKey` + `ApiKeyDto` 类型；清理 `register` 相关（低优先级） |
| `src/pages/SettingsPage.tsx` | 新增设置页 |
| `src/App.tsx` | 注册 `/settings` 路由（任意已登录用户可访问）；移除 `/register` 路由（低优先级） |
| `src/pages/RegisterPage.tsx`、`src/hooks/AuthContext.tsx`、`src/types/workflow.ts` | 低优先级：移除注册相关死代码 |

## 4. 开发阶段

### 阶段一：设置页与 API Key 管理

- 目标：用户自助查看信息与管理 API Key。
- 核心任务：
  - `SettingsPage` 展示 `getCurrentUser` 信息。
  - API Key 区：列表（`listApiKeys`）+ 创建（`createApiKey`）+ 吊销（`revokeApiKey`）+ 复制，用 `useRequest` 管理。
  - 定义 `ApiKeyDto`（字段见 §2）；列表**仅展示 `prefix`**（如 `sk-abc1…`），创建时一次性返回的明文 `key` 仅展示一次。
- 输入：`AuthController`、`api.ts` 既有封装、`frontend-code-rules.md`。
- 输出：可用设置页（路由 `/settings`，任意已登录用户可访问）。
- 验收标准：
  - 登录用户可查看信息、列出/创建/吊销 API Key。
  - 密钥仅创建时明文展示一次，之后列表只显示 `prefix`。

### 阶段二（低优先级）：清理注册入口

- 目标：去除已失效的注册功能前端入口与死代码。
- 核心任务：
  - 移除 `RegisterPage.tsx` 与 `/register` 路由。
  - 同步从 `api.ts`、`AuthContext.tsx`、`types/workflow.ts` 移除 `register`/`RegisterRequest`/`RegisterResult`，消除未使用代码（类型检查须通过）。
  - 确认登录页无「去注册」链接指向失效页。
- 输入：后端 `Register` 返回 403。

## 5. 阶段依赖图

```mermaid
flowchart LR
    S1[阶段一 设置页] --> S2[阶段二 清理注册]
```

## 6. 风险与待定项

| 风险/待定项 | 影响 | 应对策略 |
|------------|------|---------|
| 设置页路由归属 | 权限语义 | 任意已登录用户可见自身设置，不依赖权限守卫 |
| API Key 仅返回 prefix | 无法做后四位脱敏展示 | 列表只显示 `prefix`，不在计划中写无法实现的 `sk-****1234` |

## 7. 验收总标准（含验证用例）

- 设置页可用（个人信息 + API Key 管理）。
- 注册入口及死代码已清理，类型检查通过。
- 遵循前端代码规范，`npm run build` + `npm run typecheck` 通过。

**具体验证用例**：
1. 登录用户进入 `/settings`，查看个人信息；点击「创建 API Key」生成后仅显示一次明文，列表显示 `prefix`（如 `sk-abc1…`）。
2. 吊销某 API Key，列表该项标记为已吊销（或移除）；再次用该 Key 调用被拒（后端 401）。
3. 删除注册入口后，访问 `/register` 重定向或提示「注册已关闭」；`api.ts`/`AuthContext`/`types` 中无 `register` 残留，类型检查通过。

## 8. 测试要求

- 组件测试（RTL）：`SettingsPage` API Key 列表渲染、创建后列表更新、吊销交互。
- 单元测试：`api.ts` 封装 `listApiKeys`/`revokeApiKey` 的请求路径与参数正确。

## 变更记录

| 日期 | 修改人 | 修改内容 | 关联任务/PR |
|------|--------|----------|------------|
| 2026-07-15 | Agent | 初版（根目录 plan-frontend-settings.md） | 前端功能缺口审计 |
| 2026-07-15 | Agent | 迁移至 alpha/ 并按规范命名；补 listApiKeys/revokeApiKey 封装与 ApiKeyDto；加注册清理任务；补全测试/验证用例/改造点 | 计划评审 P0/P1/P2 |
| 2026-07-15 | Agent | 二轮修订：移除权限相关导航（移归模块一 RBAC，解决阶段倒挂）；ApiKeyDto 仅 prefix 展示；注册清理扩至 api.ts/AuthContext/types；错误链改 api.ts ApiError+notifications | 计划评审第二轮 P0/P1/P2 |
| 2026-07-15 | Agent | P1：移除主题切换（HeaderToolbar 已有），改入不覆盖范围说明 | 源码评审 |

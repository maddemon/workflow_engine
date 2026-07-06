# 开发计划：Cron 表达式可视化配置器（plan-ga-02-cron-builder-ui）

## 1. 概述

当前 Schedule 触发器要求用户直接编写 Cron 表达式，对非技术用户不友好。本模块实现一个类似 Windows 计划任务的可视化配置界面，让用户通过表单选择即可生成 Cron 表达式。

覆盖范围：

- CronBuilder 组件：提供预设选项 + 自定义配置。
- 支持常见场景：每分钟、每小时、每天、每周、每月。
- 高级模式：保留原始 Cron 表达式输入。
- 实时预览：显示生成的 Cron 表达式和下次触发时间。

不覆盖：复杂 Cron 语法（如 `L`、`W`、`#` 等特殊字符），需高级模式手动输入。

## 2. 交付物清单

- `CronBuilder` 组件（`frontend/src/components/ParameterPanel/fields/CronBuilder.tsx`）
- Cron 工具函数（`frontend/src/utils/cronUtils.ts`）
- 单元测试

## 3. 用户界面设计

### 3.1 配置模式

```
┌─────────────────────────────────────────────────────┐
│ Schedule Type                                        │
│ ┌─────────────────────────────────────────────────┐ │
│ │ ○ Every X Minutes                               │ │
│ │ ○ Every X Hours                                 │ │
│ │ ○ Daily at                                      │ │
│ │ ○ Weekly on                                     │ │
│ │ ○ Monthly on                                    │ │
│ │ ○ Custom (Advanced)                             │ │
│ └─────────────────────────────────────────────────┘ │
│                                                     │
│ [根据选择显示对应配置项]                              │
│                                                     │
│ ┌─────────────────────────────────────────────────┐ │
│ │ Generated: */5 * * * *                          │ │
│ │ Next: 2026-07-06 10:05:00                       │ │
│ └─────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────┘
```

### 3.2 各模式配置项

| 模式 | 配置项 | 示例 |
|------|--------|------|
| Every X Minutes | 间隔分钟数 (1-59) | 每 5 分钟 |
| Every X Hours | 间隔小时数 (1-23) | 每 2 小时 |
| Daily at | 时间 (时:分) | 每天 09:00 |
| Weekly on | 星期 + 时间 | 每周一 09:00 |
| Monthly on | 日期 + 时间 | 每月 1 号 09:00 |
| Custom | 原始 Cron 表达式 | 用户手动输入 |

## 4. Cron 工具函数

```typescript
// cronUtils.ts

interface CronPreset {
  type: 'minutes' | 'hours' | 'daily' | 'weekly' | 'monthly' | 'custom';
  // 根据 type 不同，字段不同
}

// 生成 Cron 表达式
function generateCron(preset: CronPreset): string

// 解析 Cron 表达式为预设（用于编辑时回填）
function parseCronToPreset(cron: string): CronPreset

// 获取下次触发时间（简化版，用于预览）
function getNextFireTime(cron: string): Date | null
```

### 4.1 Cron 表达式生成规则

| 模式 | 生成的 Cron |
|------|-------------|
| 每 X 分钟 | `*/X * * * *` |
| 每 X 小时 | `0 */X * * *` |
| 每天 HH:MM | `MM HH * * *` |
| 每周D HH:MM | `MM HH * * D` (D: 0=Sun, 1=Mon...) |
| 每月DD HH:MM | `MM HH DD * *` |

## 5. 实现任务

### 任务一：Cron 工具函数

- **目标**：实现 Cron 表达式生成与解析。
- **核心任务**：
  - 创建 `frontend/src/utils/cronUtils.ts`。
  - 实现 `generateCron(preset)` 函数。
  - 实现 `parseCronToPreset(cron)` 函数（基础解析）。
  - 实现 `getNextFireTime(cron)` 函数（简化计算）。
  - 编写单元测试。
- **验收标准**：
  - 各预设模式可正确生成 Cron 表达式。
  - 常见 Cron 表达式可解析为预设。
  - 测试覆盖率 ≥80%。

### 任务二：CronBuilder 组件

- **目标**：实现可视化配置界面。
- **核心任务**：
  - 创建 `frontend/src/components/ParameterPanel/fields/CronBuilder.tsx`。
  - 实现 6 种配置模式的表单。
  - 集成 `cronUtils` 生成和解析。
  - 显示生成的 Cron 表达式和下次触发时间。
  - 支持 controlled 模式（value + onChange）。
- **输入**：当前 Cron 表达式（可选）。
- **输出**：Cron 表达式字符串。
- **验收标准**：
  - 各模式可正确切换和配置。
  - 生成的 Cron 表达式正确。
  - 编辑已有触发器时正确回填。

### 任务三：集成到 TriggerConfig

- **目标**：替换现有的 Cron TextInput。
- **核心任务**：
  - 修改 `TriggerConfig.tsx`，使用 `CronBuilder` 替换 `TextInput`。
  - 保持现有 API 兼容。
- **验收标准**：
  - Schedule 触发器创建/编辑使用新界面。
  - 功能回归测试通过。

## 6. 验收总标准

- 用户可通过表单配置常见定时任务，无需了解 Cron 语法。
- 生成的 Cron 表达式正确。
- 编辑已有触发器时配置正确回填。
- 高级用户仍可手动输入 Cron 表达式。
- 单元测试通过。

## 变更记录

| 日期 | 修改人 | 修改内容 | 关联任务 |
|------|--------|----------|----------|
| 2026-07-06 | Agent | 创建 Cron 可视化配置器计划 | GA 阶段 UI 改进 |

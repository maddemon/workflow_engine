# 实体模型重构收尾计划（plan-refactor-entity-model）

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 完成实体模型重构——删除 Infrastructure/Persistence 的重复实体和 Repository，将 EF Core JSON 列配置移到 Core，去掉 Runtime 中的 NodeDefinition 映射层。

**当前状态（已完成）：**
- ✅ Core 实体：`NodeDefinition`（合并原 `NodeInstance` + `NodeDefinition`）、`Connection`、`NodeExecutionRecord` 改为 POJO（无 Entity 继承）
- ✅ `JsonColumnAttribute` 已创建
- ✅ DbContext 已移到 `Core.Data`
- ✅ Infrastructure/Persistence/Entities、Repositories、Migrations 已删除
- ✅ Repository 接口已删除（`IWorkflowRepository`、`IExecutionStore`、`ICredentialRepository`、`ITriggerRepository`）
- ✅ Application Services 已改为注入 `FlowEngineDbContext`
- ✅ 前端类型已更新（`NodeInstance` → `NodeDefinition`）
- ✅ 测试已通过（238/238）

**当前问题：** 仅 `WorkflowExecutor.cs` 仍有旧 `IExecutionStore` 引用未更新

---

### Task 1: 修复 WorkflowExecutor.cs

**Files:**
- Modify: `backend/FlowEngine.Runtime/Executor/WorkflowExecutor.cs`

**Changes:**
- `IExecutionStore` 参数类型 → `FlowEngineDbContext`
- `executionStore.UpdateStatusAsync(id, status, ct)` → `dbContext.ExecutionRecords.Where(e => e.Id == id).ExecuteUpdateAsync(s => s.SetProperty(e => e.Status, status), ct)`
- `executionStore.SaveAsync(execution, ct)` → `dbContext.SaveChangesAsync(ct)`
- `executionStore.AddNodeRecordAsync(id, record, ct)` → 删除（record 已加到 execution.NodeRecords）
- `scopedExecutionStore` → `scopedDbContext`
- 添加 `using Microsoft.EntityFrameworkCore;` 和 `using FlowEngine.Core.Data;`

详细改动：

**1. 构造函数和字段：**
```
private readonly IWorkflowRepository _workflowRepository;   // → private readonly FlowEngineDbContext _dbContext;
private readonly IExecutionStore _executionStore;            // → 删除
```

**2. StartAsync 方法：**
```
_workflowRepository.GetByIdAsync(...) → _dbContext.Workflows.FirstOrDefaultAsync(...)
_executionStore.SaveAsync(...)        → _dbContext.ExecutionRecords.Add(...); _dbContext.SaveChangesAsync(...)
```

**3. Task.Run 内部：**
```
var scopedExecutionStore = scope.ServiceProvider.GetRequiredService<IExecutionStore>();
→ var scopedDbContext = scope.ServiceProvider.GetRequiredService<FlowEngineDbContext>();
var loadedExecution = await scopedDbContext.ExecutionRecords.FirstOrDefaultAsync(e => e.Id == executionRecord.Id, default);
scopedExecutionStore.UpdateStatusAsync(...) → scopedDbContext.ExecutionRecords.Where(...).ExecuteUpdateAsync(...)
```

**4. ExecuteLoopAsync 及子方法：**
```
IExecutionStore executionStore 参数 → FlowEngineDbContext executionStore
executionStore.UpdateStatusAsync(...) → executionStore.ExecutionRecords.Where(...).ExecuteUpdateAsync(...)
executionStore.SaveAsync(...) → executionStore.SaveChangesAsync(...)
executionStore.AddNodeRecordAsync(...) → 删除此行
```

**5. 所有方法签名中的 `NodeInstance` → `NodeDefinition`（部分已改好，确认即可）**

- [ ] Step 1: 在 WorkflowExecutor.cs 顶部添加 `using FlowEngine.Core.Data;` 和 `using Microsoft.EntityFrameworkCore;`
- [ ] Step 2: 替换构造函数注入和字段（`IWorkflowRepository` + `IExecutionStore` → `FlowEngineDbContext`）
- [ ] Step 3: 替换 StartAsync 中的 `_workflowRepository` 调用
- [ ] Step 4: 替换 Task.Run 块中的 `scopedExecutionStore` → `scopedDbContext` + 加载 execution
- [ ] Step 5: 替换 ExecuteLoopAsync 签名和内部调用
- [ ] Step 6: 替换 ProcessNodeAsync 签名和内部调用
- [ ] Step 7: 替换 ProcessTimeoutsAsync 签名和内部调用
- [ ] Step 8: 确认所有 `NodeInstance` → `NodeDefinition` 已改好
- [ ] Step 9: `dotnet build` 验证通过
- [ ] Step 10: 运行所有测试 `dotnet test` 确认通过

### Task 2: 删除 IExecutionStore 接口和相关实现

**Files:**
- Delete: `backend/FlowEngine.Core/Abstractions/IExecutionStore.cs`
- Delete: `tests/FlowEngine.Runtime.Tests/Executor/InMemoryExecutionStore.cs`
- Modify: `backend/FlowEngine.Application/Executions/ExecutionService.cs`
- Modify: `backend/FlowEngine.Runtime/Executor/NodeExecutionContextFactory.cs`
- Modify: `backend/FlowEngine.Core/Entities/NodeExecutionContext.cs`

**Changes:**
- 删除 `IExecutionStore` 接口文件
- 删除 `InMemoryExecutionStore` 测试实现
- 修改 `ExecutionService.cs`，移除 `IExecutionStore` 依赖
- 修改 `NodeExecutionContextFactory.cs`，移除 `IExecutionStore` 依赖
- 修改 `NodeExecutionContext.cs`，移除 `ExecutionStore` 属性

- [ ] Step 1: 删除 `IExecutionStore.cs` 文件
- [ ] Step 2: 删除 `InMemoryExecutionStore.cs` 文件
- [ ] Step 3: 修改 `ExecutionService.cs`，移除 `IExecutionStore` 依赖
- [ ] Step 4: 修改 `NodeExecutionContextFactory.cs`，移除 `IExecutionStore` 依赖
- [ ] Step 5: 修改 `NodeExecutionContext.cs`，移除 `ExecutionStore` 属性
- [ ] Step 6: `dotnet build` 验证通过

### Task 3: 更新测试代码

**Files:**
- Modify: `tests/FlowEngine.Runtime.Tests/Executor/WorkflowExecutorTests.cs`
- Modify: `tests/FlowEngine.Runtime.Tests/Plugins/AgentNodeTests.cs`

**Changes:**
- 移除 `TestScopeFactory`、`TestScope`、`TestServiceProvider` 中的 `IExecutionStore` 依赖
- 更新 `AgentNodeTests.cs` 中的 `InMemoryTestExecutionStore`

- [ ] Step 1: 修改 `WorkflowExecutorTests.cs`，移除 `IExecutionStore` 相关类
- [ ] Step 2: 修改 `AgentNodeTests.cs`，移除 `InMemoryTestExecutionStore`
- [ ] Step 3: `dotnet test` 验证通过

### Task 4: 验证 Infrastructure 项目清理

**Files:**
- Check: `backend/FlowEngine.Infrastructure/Persistence/` 目录为空
- Check: `backend/FlowEngine.Core/Abstractions/` 中无 `IWorkflowRepository`、`ICredentialRepository`、`ITriggerRepository`

- [ ] Step 1: 确认 Entities、Repositories、Migrations 目录已被删除
- [ ] Step 2: 确认 DbContext 已完全删除（旧文件在 Infrastructure，新文件在 Core.Data）
- [ ] Step 3: 确认无旧接口文件残留

### Task 5: 前端验证

- [ ] Step 1: `npm run build` 通过
- [ ] Step 2: 确认 `NodeInstance` → `NodeDefinition` 重命名无遗漏

### Task 6: 最终验证

- [ ] Step 1: `dotnet build` 全项目通过
- [ ] Step 2: `dotnet test` 全部通过
- [ ] Step 3: `npx tsc --noEmit` 前端无类型错误
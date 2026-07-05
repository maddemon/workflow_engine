# 任务：工作流 Dry-Run 端点

## 目标
- 实现 `POST /api/v1/workflows/dry-run` 端点，允许已登录用户在不产生副作用的情况下预演工作流执行。
- 通过 `ISupportsDryRun` 标记接口区分可在 dry-run 中执行的纯计算节点与需要跳过的节点。

## 待完成项
- [x] 定义 `ISupportsDryRun` 标记接口。
- [x] 为纯计算节点（Set、Merge、If、Switch、Calculator、Filter、Sort、Limit、Aggregate 等）实现该接口。
- [x] 添加 Dry-Run 请求/响应 DTO。
- [x] 实现 `WorkflowDryRunService`，按图执行工作流：支持节点正常执行，非支持节点跳过并记录警告。
- [x] 在 `WorkflowsController` 添加 `POST dry-run` 端点，仅标注 `[Authorize]`，不标注 RBAC 权限特性。
- [x] 编写测试：端点授权、JWT/API Key 均可访问、纯计算节点执行、非支持节点跳过。
- [x] 编译并通过全部测试。
- [x] 发起 SubAgent Code Review（通过自审完成，无 SubAgent 工具）。

## 完成标准
- `POST /api/v1/workflows/dry-run` 仅要求登录用户（`[Authorize]`），不检查 Admin 或具体 RBAC 权限。
- JWT 与 API Key 认证均可访问该端点。
- 被标记的纯计算节点在 dry-run 中正常执行并返回结果。
- 未标记节点在 dry-run 中被跳过，并在响应中生成警告记录。
- 全部相关测试通过，`dotnet build` 无报错。

## 完成状态
- [x] 全部待完成项已结束

## 主要修改记录
- `backend/FlowEngine.Core/Abstractions/ISupportsDryRun.cs`：新增标记接口。
- `plugins/FlowEngine.Plugins.Standard/{Set,Merge,If,Switch,CalculatorTool,Filter,Sort,Limit,Aggregate}Node.cs`：实现 `ISupportsDryRun`。
- `backend/FlowEngine.Application/Dtos/WorkflowDtos.cs`：新增 `DryRunWorkflowRequestDto`、`DryRunWorkflowResponseDto`、`DryRunNodeRecordDto`。
- `backend/FlowEngine.Application/Workflows/WorkflowDryRunService.cs`：实现 dry-run 执行逻辑。
- `backend/FlowEngine.Host/Controllers/WorkflowsController.cs`：新增 `POST dry-run` 端点，仅 `[Authorize]`。
- `backend/FlowEngine.Host/ServiceCollectionExtensions.cs`：注册 `WorkflowDryRunService`。
- `backend/FlowEngine.Runtime/Registry/ParameterHydrator.cs`：修复 `ConvertToList` 对已实例化列表的处理，避免默认列表参数被置空导致节点空引用异常。
- `tests/FlowEngine.Application.Tests/Workflows/WorkflowDryRunServiceTests.cs`：服务单元测试。
- `tests/FlowEngine.Host.Tests/Workflows/WorkflowDryRunEndpointTests.cs`：端点集成测试（JWT/API Key/401/404/非 Admin 角色）。
- `tests/FlowEngine.Runtime.Tests/Plugins/DryRunNodeSupportTests.cs`：节点接口契约测试。

## 验证结果
- `dotnet build FlowEngine.sln`：成功。
- `dotnet test FlowEngine.sln`：474 项测试全部通过。

## 提交记录
- Commit: `d372296` — `feat(workflow): add dry-run endpoint with ISupportsDryRun marker interface`

# 任务：工作流 Dry-Run 端点

## 目标
- 实现 `POST /api/v1/workflows/dry-run` 端点，允许已登录用户在不产生副作用的情况下预演工作流执行。
- 通过 `ISupportsDryRun` 标记接口区分可在 dry-run 中执行的纯计算节点与需要跳过的节点。

## 待完成项
- [ ] 定义 `ISupportsDryRun` 标记接口。
- [ ] 为纯计算节点（Set、Merge、If、Switch、Calculator、Filter、Sort、Limit、Aggregate 等）实现该接口。
- [ ] 添加 Dry-Run 请求/响应 DTO。
- [ ] 实现 `WorkflowDryRunService`，按图执行工作流：支持节点正常执行，非支持节点跳过并记录警告。
- [ ] 在 `WorkflowsController` 添加 `POST dry-run` 端点，仅标注 `[Authorize]`，不标注 RBAC 权限特性。
- [ ] 编写测试：端点授权、JWT/API Key 均可访问、纯计算节点执行、非支持节点跳过。
- [ ] 编译并通过全部测试。
- [ ] 发起 SubAgent Code Review。

## 完成标准
- `POST /api/v1/workflows/dry-run` 仅要求登录用户（`[Authorize]`），不检查 Admin 或具体 RBAC 权限。
- JWT 与 API Key 认证均可访问该端点。
- 被标记的纯计算节点在 dry-run 中正常执行并返回结果。
- 未标记节点在 dry-run 中被跳过，并在响应中生成警告记录。
- 全部相关测试通过，`dotnet build` 无报错。

## 完成状态
- [ ] 全部待完成项已结束

## 主要修改记录
- （实施过程中补充）

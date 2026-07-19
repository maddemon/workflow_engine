# 任务：后端 Host 控制器测试补充

## 目标

将 `FlowEngine.Host` 行覆盖率从 **58.9%**（Task 008 实测，覆盖行 2043 / 3469）提升至 **75%+**（后端整体 75% 标准）。距目标 ~16pt，高投入；本 Phase 可用 Moq（`FlowEngine.Host.Tests` 已引用 Moq）。

**行号说明**：文中 `:行号`（如 `:15`）取自 2026-07-17 版本源码，仅作辅助参考；执行时请以类名 / 方法名 / 签名为准确认当前源码，行号可能因后续改动偏移。

## 目标类与已核实 API

### ProjectsController
- 命名空间 `FlowEngine.Host.Controllers`，`Controllers/ProjectsController.cs:15`
- 主构造：**`(ProjectService projectService)`**
- 动作（所有 id 为 **`Guid`**，路由 `{id:guid}`）：
  - `GetAll` `[HttpGet]` → `Task<ActionResult<IReadOnlyList<ProjectDto>>>`
  - `GetById(Guid id, …)` `[HttpGet("{id:guid}")]` → `Task<ActionResult<ProjectDto>>`
  - `Create([FromBody] CreateProjectDto dto, …)` `[HttpPost]`

### FilesController
- 命名空间 `FlowEngine.Host.Controllers`，`Controllers/FilesController.cs:21`
- 主构造：**`(FileService fileService, IEventBus eventBus, AuditEventFactory auditFactory, IStringLocalizer<SharedResource> localizer)`**
- 动作（所有 id 为 **`Guid`**）：
  - `Upload(IFormFile file, [FromQuery] Guid projectId, …)` `[HttpPost("upload")]`
  - `GetById(Guid id, …)` `[HttpGet("{id:guid}")]`
  - `Download(Guid id, …)` `[HttpGet("{id:guid}/download")]`

## 待完成项

> **Moq 兼容性前置（Issue #7）**：执行前先跑一遍现有 Host 测试 `dotnet test tests/FlowEngine.Host.Tests` 确认 Moq 4.x 与 xUnit v3（3.2.2）环境正常，避免新增测试时才发现框架冲突，再写本 Phase 测试。

- [ ] **5.1 ProjectsController 测试**：用 Moq mock `ProjectService`，覆盖 `GetAll` / `GetById(Guid)`（命中与未命中返回 404）/ `Create`（合法 DTO 返回 200、非法返回 400）。**id 一律用 `Guid`，非 string**。
- [ ] **5.2 FilesController 测试**：用 Moq mock `FileService` / `IEventBus` / `AuditEventFactory` / `IStringLocalizer<SharedResource>`；覆盖 `Upload`（含 `projectId` 为 `Guid`）、`GetById`、`Download` 正常与缺参路径。

## 完成标准

- `dotnet test tests/FlowEngine.Host.Tests` 全绿。
- 仅本 Phase 可使用 Moq；不使用 `FluentAssertions`（统一 `Assert.*`）。
- 所有签名与上文核实一致；id 类型严格为 `Guid`。

- 对应项目 `dotnet build` 通过（无编译错误，新增测试不得引入类型/签名错误）。

## 完成状态

- [x] 5.1
- [x] 5.2

## 主要修改记录

- 重写自 `plan-unit-test-coverage.md`：明确两控制器真实构造依赖与 `Guid` id；原草稿构造函数与 id 类型（string）错误。

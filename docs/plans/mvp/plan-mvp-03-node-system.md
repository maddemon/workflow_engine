# 开发计划：节点系统与注册中心（plan-mvp-03-node-system）

## 1. 概述

实现节点注册中心，负责扫描 `plugins/` 目录下的 DLL、反射查找 `INodeType` 实现、隔离加载、缓存元数据、按类型名创建实例，并通过 API 暴露节点类型描述供前端渲染。

覆盖范围：
- `INodeRegistry` 实现（扫描、反射、隔离加载、元数据缓存、实例创建）。
- `GET /api/node-types` 端点（返回节点类型描述列表）。
- 单插件加载失败的异常隔离。

不覆盖范围：具体节点插件实现（见 plan-mvp-07 标准节点）、节点执行逻辑（见 plan-mvp-05 执行引擎）。

## 2. 交付物清单

- `src/FlowEngine.Runtime/Registry/NodeRegistry.cs`（实现 `INodeRegistry`）。
- `src/FlowEngine.Runtime/Registry/PluginLoader.cs`（DLL 扫描与 AssemblyLoadContext 隔离加载）。
- `src/FlowEngine.Runtime/Registry/NodeTypeDescriptor.cs`（节点类型元数据缓存模型）。
- `src/FlowEngine.Host/Controllers/NodeTypesController.cs`（`GET /api/v1/node-types` 端点）。
- 单元测试：扫描空目录、扫描含合法插件目录、扫描含损坏插件目录。

## 3. 开发阶段

### 阶段一：注册中心与 DLL 扫描

- 目标：实现节点注册中心，启动时扫描 `plugins/` 并加载所有 `INodeType` 实现。
- 核心任务：
  - 实现 `PluginLoader`：遍历 `plugins/*.dll`，使用独立 `AssemblyLoadContext` 加载每个 DLL。
  - 反射查找实现了 `INodeType` 的非抽象类。
  - 实例化一次以读取元数据（TypeName/DisplayName/Category/Icon/Parameters/Ports/ExecutionMode），缓存到 `NodeTypeDescriptor`。
  - 实现按 `TypeName` 创建新实例的能力（每次执行创建新实例，避免状态污染）。
  - 实现 `INodeRegistry.GetDescriptors()` 返回所有节点类型描述。
  - 实现 `INodeRegistry.CreateNodeInstance(string typeName)` 创建节点实例。
- 输入：[node-system.md](../../architecture/node-system.md) §3 注册流程、§3.1 注册中心职责、§3.2 DLL 加载技术要点、§7 节点实例化注意事项。
- 输出：可扫描并加载插件的注册中心。
- 验收标准：
  - 空目录扫描不抛异常，返回空列表。
  - 含合法插件的目录扫描后，`GetDescriptors()` 返回对应数量描述。
  - 元数据缓存生效（同一类型不重复实例化读取元数据）。
  - `CreateNodeInstance` 每次返回新实例。
- 依赖：plan-mvp-02 Core 抽象、plan-mvp-01 项目骨架。

### 阶段二：隔离加载与异常处理

- 目标：单插件加载失败不影响主程序与其他插件。
- 核心任务：
  - 捕获 `AssemblyLoadContext.Load` 抛出的异常（`FileLoadException`/`BadImageFormatException`/`TypeLoadException` 等）。
  - 记录警告日志，包含失败的 DLL 文件名与异常消息。
  - 跳过失败插件，继续加载其他插件。
  - 处理类型名冲突（同一 `TypeName` 出现多次时记录警告，保留首个）。
- 输入：[node-system.md](../../architecture/node-system.md) §3.2、[overview.md](../../architecture/overview.md) §8 安全边界。
- 输出：健壮的插件加载流程。
- 验收标准：
  - 放入一个损坏 DLL 与一个合法 DLL，主程序正常启动。
  - 损坏 DLL 在日志中记录警告，合法 DLL 正常加载。
  - 类型名冲突时记录警告，不抛异常。
- 依赖：阶段一。

### 阶段三：节点类型查询 API

- 目标：通过 HTTP 暴露节点类型描述。
- 核心任务：
  - 实现 `NodeTypesController`，提供 `GET /api/v1/node-types` 端点。
  - 返回所有节点类型描述的 JSON 列表（TypeName/DisplayName/Category/Icon/Parameters/Ports/ExecutionMode）。
  - JSON 字段使用 camelCase（遵循 [terminology.md](../../architecture/terminology.md) §9.4）。
  - 支持按 `category` 查询参数过滤。
- 输入：[node-system.md](../../architecture/node-system.md) §6 冷启动加载流程、§4 参数定义驱动 UI。
- 输出：可被前端调用的节点类型 API。
- 验收标准：
  - `GET /api/v1/node-types` 返回 200 与 JSON 数组。
  - JSON 字段为 camelCase。
  - 按 `?category=Core` 过滤生效。
- 依赖：阶段一。

### 阶段四：注册中心集成到 Host

- 目标：在 Host 启动时初始化注册中心。
- 核心任务：
  - 在 `Program.cs` 中注册 `INodeRegistry` 为单例。
  - 启动时触发一次扫描（可通过 `IHostedService` 或在 DI 注册后立即调用）。
  - 扫描结果记录到日志（加载了多少插件、多少节点类型）。
- 输入：plan-mvp-01 项目骨架的 Program.cs。
- 输出：Host 启动后注册中心就绪。
- 验收标准：
  - Host 启动后日志显示扫描结果。
  - `GET /api/v1/node-types` 可立即返回结果。
- 依赖：阶段三、plan-mvp-01。

## 4. 阶段依赖图

```mermaid
flowchart LR
    S1[阶段一 注册中心与扫描] --> S2[阶段二 隔离加载与异常处理]
    S1 --> S3[阶段三 节点类型查询 API]
    S3 --> S4[阶段四 注册中心集成到 Host]
    S2 --> S4
```

## 5. 风险与待定项

| 风险/待定项 | 影响 | 应对策略 |
|------------|------|---------|
| AssemblyLoadContext 在 .NET 8 下的 API 变化 | 加载失败 | 使用 `System.Runtime.Loader.AssemblyLoadContext`，参考官方文档 |
| 插件 DLL 依赖主程序未提供的共享库 | 加载时 TypeLoadException | 捕获异常并记录，要求插件自包含依赖 |
| 元数据缓存未失效 | 节点类型更新后不生效 | MVP 阶段重启服务刷新缓存，Alpha 阶段支持热重载 |
| 节点类型名大小写敏感 | 前端匹配失败 | TypeName 统一小写驼峰，注册时归一化 |

## 6. 验收总标准

- 放入 HTTP/Code/If 三个标准节点插件后，`GET /api/v1/node-types` 返回 3 个类型（依赖 plan-mvp-07 完成后验证）。
- 插件 DLL 加载失败时记录警告，主程序不崩溃。
- 注册中心实现 `INodeRegistry` 接口，签名与 [node-system.md](../../architecture/node-system.md) §3 一致。
- 单元测试覆盖空目录、合法插件、损坏插件三种场景。

## 变更记录

| 日期 | 修改人 | 修改内容 | 关联任务 |
|------|--------|----------|----------|
| 2026-06-18 | Agent | 创建节点系统计划 | MVP-0 |

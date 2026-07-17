# 任务：后端 Host 中间件测试补充

## 目标

补充 `FlowEngine.Host` 中间件单元测试，并入 Host 模块 **75%+** 目标（后端整体 75% 标准，Task 008 实测 Host 基线 58.9%）。本 Phase 可用 Moq（`FlowEngine.Host.Tests` 已引用）。

**行号说明**：文中 `:行号`（如 `:6`）取自 2026-07-17 版本源码，仅作辅助参考；执行时请以类名 / 方法名 / 签名为准确认当前源码，行号可能因后续改动偏移。

## 目标类与已核实 API

### SecurityHeadersMiddleware
- 命名空间 `FlowEngine.Host.Middlewares`，`Middlewares/SecurityHeadersMiddleware.cs:6`
- 主构造：**`(RequestDelegate next, IWebHostEnvironment environment)`**（**非仅 `RequestDelegate`**，无 options 参数，原草稿错误）。
- `public async Task InvokeAsync(HttpContext context)` :13

### GlobalExceptionHandlerMiddleware
- 命名空间 `FlowEngine.Host.Middlewares`，`Middlewares/GlobalExceptionHandlerMiddleware.cs:10`
- 主构造：**`(RequestDelegate next, ILogger<GlobalExceptionHandlerMiddleware> logger)`**（logger 须为 `ILogger<GlobalExceptionHandlerMiddleware>` 类型）。
- `public async Task InvokeAsync(HttpContext context)` :19

## 待完成项

> **Moq 兼容性前置（Issue #7）**：执行前先跑一遍现有 Host 测试 `dotnet test tests/FlowEngine.Host.Tests` 确认 Moq 4.x 与 xUnit v3（3.2.2）环境正常，避免新增测试时才发现框架冲突，再写本 Phase 测试。

- [ ] **6.1 SecurityHeadersMiddleware 测试**：构造传入 `RequestDelegate`（如 `ctx => Task.CompletedTask`）与 `IWebHostEnvironment`（Moq mock）；调用 `InvokeAsync` 后断言 `HttpContext.Response.Headers` 含预期安全头（如 `X-Content-Type-Options` / `X-Frame-Options` 等，以源码实际写入为准）。
- [ ] **6.2 GlobalExceptionHandlerMiddleware 测试**：构造传入 `RequestDelegate`（在内部抛 `Exception`）与 `ILogger<...>`（Moq）；调用 `InvokeAsync` 后断言响应状态码被置为 500（或源码约定的错误码）且未向上冒泡。

## 完成标准

- `dotnet test tests/FlowEngine.Host.Tests` 全绿。
- 仅本 Phase 可使用 Moq；不使用 `FluentAssertions`（统一 `Assert.*`）。
- 所有签名与上文核实一致。

- 对应项目 `dotnet build` 通过（无编译错误，新增测试不得引入类型/签名错误）。

## 完成状态

- [ ] 6.1
- [ ] 6.2

## 主要修改记录

- 重写自 `plan-unit-test-coverage.md`：修正 `SecurityHeadersMiddleware` 构造（补 `IWebHostEnvironment`）、`GlobalExceptionHandlerMiddleware` 的 `ILogger<…>` 类型要求。

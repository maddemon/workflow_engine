# 单机部署（Single-Machine Deployment）

> 本文档基于当前代码编写，以代码为准。涉及端口、配置键、文件路径以源码为权威来源。

## 1. 形态概述

Flow Engine 以**单一 .NET 后台服务进程**形态运行，启动后同时承载以下能力，无需额外部署其他组件：

- **HTTP API**：REST 端点（前缀 `/api/v1`），见 [REST API 概览](reference/rest-api.md)。
- **前端 SPA**：构建产物托管于 `backend/FlowEngine.Host/wwwroot`，由 `UseStaticFiles` + `MapFallbackToFile("index.html")` 提供。
- **执行引擎**：工作流执行在进程内完成（无独立 worker）。
- **Quartz 调度器**：触发定时（Schedule）与轮询（Poll）触发器；启动时恢复已激活触发器的调度。
- **Webhook 路由**：以 HMAC 签名鉴权的动态路由中间件（`WebhookRoutingMiddleware`），支持运行时增删路由。
- **MCP 端点**：`/mcp`（Streamable HTTP，需授权），见 [MCP 工具参考](reference/mcp-tools.md)。

整体形态与 [系统总览](architecture/overview.md) 一致。

## 2. 默认端口

来自 `backend/FlowEngine.Host/Properties/launchSettings.json`：

| 服务 | 地址 | 说明 |
|------|------|------|
| 后端 HTTP | `http://localhost:8001` | 默认 `http` 启动档案 |
| 后端 HTTPS | `https://localhost:8002`（与 `http://localhost:8001` 并存） | `https` 启动档案 |
| 前端 dev server | `:4000` | 仅开发期；将 `/api` 代理到 `:8001` |

> 部署到生产时，端口由宿主（反向代理 / systemd / Docker `-p`）决定，不依赖 `launchSettings.json`。HTTPS 证书建议由反向代理（Nginx / Caddy）终止。

## 3. 零配置启动（首次启动做了什么）

进程启动（`Program.cs` → `UseFlowEngineAsync` → `UseInitialization`）时自动完成：

1. **数据库初始化**：调用 `FlowEngineDbContext.Database.MigrateAsync()` 应用 EF Core 迁移，首次启动即创建数据库并升级结构。
2. **插件扫描与加载**：`PluginLoader.LoadNodes()` 枚举 `plugins/` 目录下所有 `*.dll`，经独立 `AssemblyLoadContext` 加载并注册到节点注册中心。单个插件加载失败仅记警告，不影响主程序启动。
3. **触发器恢复**：读取已激活的 Schedule / Poll 触发器，重新注册到 Quartz。
4. **健康检查就绪**：`/health` 与 `/health/ready`（含数据库连通性探测）可用。

## 4. 数据库

默认 **SQLite（零配置）**，由 `appsettings.json` 控制：

```json
{
  "Database": { "Provider": "sqlite" },
  "ConnectionStrings": {
    "Default": "Data Source=App_Data/flowengine.db;Mode=ReadWriteCreate;Cache=Shared"
  }
}
```

- 无需任何外部数据库服务即可运行。
- 切换 Provider 即可改用 **PostgreSQL / MySQL**（EF Core 提供器已接入，见 [横向扩展路径](deployment/scaling.md)）。

## 5. 关键配置（`appsettings.json`）

| 配置节 | 作用 | 备注 |
|--------|------|------|
| `Database` / `ConnectionStrings` | 数据库提供器与连接串 | 默认 SQLite |
| `Plugins:Path` | 插件 DLL 目录 | 默认 `../../plugins` |
| `Storage` / `FileStorage` | 文件存储类型与根路径 | 默认 `LocalFileSystem`，`./storage/files` |
| `Audit:LogPath` | 审计日志落盘目录 | 默认 `./storage/audit` |
| `Jwt` | JWT 签发密钥 / 有效期 | `Secret` 生产环境**必须**显式配置 |
| `Cors:AllowedOrigins` | 跨域白名单 | 默认含 `http://localhost:4000` |
| `RateLimiting` | 登录 / 注册 / API 限流 | 含 `/health` 白名单 |
| `EngineDefaults` | 默认超时 / 重试 / 退避 | — |
| `ExecutionCleanup` | 执行记录留存清理 | 默认 30 天 / 上限 1 万条 |
| `Webhook:PollingIntervalMs` | Webhook 轮询间隔 | — |
| `Logging` / `Serilog` / `OpenTelemetry` | 日志与追踪 | 默认 Console 输出 / stdout 导出 |

> 配置覆盖优先级遵循 .NET 默认：`appsettings.json` < `appsettings.{Environment}.json` < 环境变量 < 命令行参数。

## 6. 前端托管方式

后端在认证/授权中间件之前调用 `UseStaticFiles()` 提供 `wwwroot` 静态资源（index.html / js / css，匿名可访问）；非 `/api` 前缀的路径经 `MapFallbackToFile("index.html").AllowAnonymous()` 回退，保证 SPA 前端路由刷新不 404。

```csharp
// ApplicationBuilderExtensions.cs（节选）
app.UseStaticFiles();
app.MapFallbackToFile(
    "{*path:regex(^(?!api(?:/|$)).*$)}",
    "index.html").AllowAnonymous();
```

## 7. 反向代理（推荐生产形态）

生产建议前端与 API 共用一个域名，由反向代理终止 TLS 并转发：

```
Nginx / Caddy  :443
   ├── /        → 静态资源（也可直接由后端 wwwroot 提供）
   ├── /api/*   → http://localhost:8001
   ├── /mcp     → http://localhost:8001（需带 Authorization: Bearer <apiKey>）
   └── /ws/*    → WebSocket 升级（/ws/execution 转发到后端）
```

> 注意：`UseForwardedHeaders()` 已在管道早期启用，反向代理需正确设置 `X-Forwarded-For` / `X-Forwarded-Proto`，否则基于客户端 IP 的限流可能被绕过。

## 8. 进程管理

- 以 `dotnet FlowEngine.Host.dll` 直接运行，或由 systemd / Docker / k8s 托管。
- 进程内执行引擎 + Quartz 同生命周期；进程退出会中止进行中的执行（当前**无**断点续跑 / 恢复实现，代码未提供在途执行持久化与续跑）。

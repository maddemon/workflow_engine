# 运行与调试（Run & Debug）

> 本文档基于当前代码编写，以代码为准。端口、配置路径、端点映射均取自 `launchSettings.json`、`appsettings.json`、`vite.config.ts` 与 `ApplicationBuilderExtensions.cs`。

## 1. 后端（.NET 10）

### 1.1 启动命令

```bash
dotnet run --project backend/FlowEngine.Host
```

启动配置文件见 `backend/FlowEngine.Host/Properties/launchSettings.json`，默认 `http` 与 `https` 两个 profile，开发环境为 `Development`：

| Profile | 地址 | 说明 |
|---------|------|------|
| `http`  | `http://localhost:8001`                     | HTTP API |
| `https` | `https://localhost:8002;http://localhost:8001` | HTTPS + HTTP |

> 也可用 `dotnet build` 后直接运行生成的 `backend/FlowEngine.Host.dll`。开发期推荐 `dotnet run`，会自动刷新启动配置。

### 1.2 关键端点

- **HTTP API**：`:8001`（与 `:8002` 的 HTTPS 同源）。
- **MCP 端点**：`/mcp`（`ApplicationBuilderExtensions.cs:95` 的 `app.MapMcp("/mcp").RequireAuthorization()`），与后端同地址，需登录后鉴权。详见 [系统总览](architecture/overview.md) 第 10 节。
- **前端静态资源**：构建后由 `backend/FlowEngine.Host/wwwroot` 托管（`UseStaticFiles` + `MapFallbackToFile("index.html")`）。

### 1.3 首次启动发生了什么

1. 按 `appsettings.json` 的 `ConnectionStrings.Default`（`Data Source=App_Data/flowengine.db;Mode=ReadWriteCreate;...`）创建 **SQLite 数据库**文件（位于 Host 内容根目录下的 `App_Data/`）。
2. 按 `Plugins:Path`（默认 `"../../plugins"`，相对 Host 内容根解析，见 `ServiceCollectionExtensions.cs:388`）**扫描 `plugins/` 目录**，逐个 DLL 经独立 `AssemblyLoadContext` 加载节点类型（见 `PluginLoader.LoadNodes`）。

> 数据库建表由 EF Core 迁移完成：启动时调用 `FlowEngine.Migrations` 程序集的 `dbContext.Database.MigrateAsync()`（`MigrationsExtensions.cs:27`）自动套用迁移，连接串 `Mode=ReadWriteCreate` 会在库文件不存在时按需创建文件再建表。`EnsureCreated()` 不在代码路径中。

### 1.4 配置（appsettings.json）

配置文件位于 `backend/FlowEngine.Host/appsettings.json`。常用节区：

| 节区 | 作用 |
|------|------|
| `Database` / `ConnectionStrings` | 选择提供器（默认 `sqlite`）与连接串 |
| `Plugins:Path` | 插件目录（相对 Host 内容根，默认 `../../plugins`） |
| `Expression:EnvironmentWhitelist` | 表达式/脚本沙箱允许的全局对象白名单 |
| `Cors:AllowedOrigins` | 跨域来源（默认含 `http://localhost:4000`） |
| `Logging` / `Serilog` | 日志级别（默认 `Information`） |
| `Jwt` | 登录签发密钥（首次部署需自行配置 `Secret`） |

### 1.5 日志约定

- 统一使用 `ILogger<T>`，**禁止 `Console.WriteLine`**（后端代码规范第 10 条）。
- 插件加载失败时仅记 `LogWarning`，不影响主程序启动（见 `PluginLoader.LoadNodes` 的多个 `catch`）。

### 1.6 调试

- 在 IDE（Rider / VS / VS Code）中以 `FlowEngine.Host` 作为启动项目附加调试器；`launchSettings.json` 已声明 `applicationUrl`。
- 以 `https` profile 启动会自动在浏览器打开 Swagger/前端（取决于 `launchBrowser`）。
- 表达式求值、节点执行均运行在引擎管线内，断点可设在具体节点的 `ExecuteAsync` 或 `FlowEngine.Runtime` 的执行器。

## 2. 前端（React 19 + Vite）

### 2.1 启动命令

```bash
cd frontend
npm install   # 首次
npm run dev    # Vite dev server
```

`frontend/vite.config.ts`：开发服务器监听 `:4000`，并将 `/api` 代理到后端 `http://localhost:8001`：

```ts
server: {
  port: 4000,
  proxy: {
    "/api": { target: "http://localhost:8001", changeOrigin: true },
  },
},
```

> 因此本地联调时：浏览器访问 `http://localhost:4000`，API 请求经代理落到 `:8001`，无需手动处理跨域。

### 2.2 构建与托管

```bash
npm run build   # 输出到 backend/FlowEngine.Host/wwwroot
```

构建产物由后端统一托管；生产部署只需运行后端，无需单独的前端服务器。

### 2.3 调试

- 浏览器开发者工具查看 WebSocket（执行实时视图）与 `/api` 网络请求。
- 前端请求统一经 `services/api.ts` 的 axios 封装与 `ApiError` 拦截器，401 跳登录、其余经 `notifications.show` 提示。

## 3. 常见排查

| 现象 | 可能原因 |
|------|----------|
| 节点面板缺少某节点 | 插件 DLL 未放入 `plugins/` 或被 `PluginLoader` 因框架不兼容/哈希白名单跳过（查 `LogWarning`） |
| `/api` 404 | 前端 dev server 未启动，或代理目标后端未运行 |
| MCP 返回 401 | `/mcp` 需登录鉴权，未携带有效令牌 |
| 数据库锁 | SQLite 多进程并发，确认仅一个后端实例在跑 |

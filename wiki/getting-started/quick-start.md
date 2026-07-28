# 快速上手

本页提供三种常见运行方式的可复制命令。环境要求见 [安装与环境要求](installation.md)，整体架构见 [系统总览](architecture/overview.md)。

## 端口总览

| 服务 | 地址 | 说明 |
|------|------|------|
| 后端 HTTP | `http://localhost:8001` | REST API 与 SPA 静态资源宿主（生产构建后由后端直接托管前端）。 |
| 后端 HTTPS | `https://localhost:8002` | 开发期可选 HTTPS 端点。 |
| 前端开发服务器 | `http://localhost:4000` | Vite 开发模式，内置 `/api` 代理到 `http://localhost:8001`。 |
| MCP 端点 | `http://localhost:8001/mcp` | Model Context Protocol（Streamable HTTP），需认证。 |

> Vite 代理配置见 `frontend/vite.config.ts`：`server.port = 4000`，`/api` → `http://localhost:8001`，`build.outDir = ../backend/FlowEngine.Host/wwwroot`。

---

## 1. 运行后端

在仓库根目录执行（仓库含 `FlowEngine.sln`）：

```bash
# 设置管理员密码（首次启动必填）
export FLOWENGINE_ADMIN_PASSWORD="your-strong-password-12+"

# 运行后端宿主项目
dotnet run --project backend/FlowEngine.Host
```

启动成功后访问 `http://localhost:8001`。首次启动会自动应用数据库迁移、创建 SQLite 库并扫描 `plugins/` 节点插件。

> 如需执行生产构建，见第 3 节。

---

## 2. 前端开发模式

新建一个终端，进入前端目录安装依赖并启动 Vite 开发服务器（需后端已在 `:8001` 运行，以便 `/api` 代理生效）：

```bash
cd frontend
npm install
npm run dev
```

启动后访问 `http://localhost:4000`，浏览器请求 `/api/*` 会自动转发到后端 `http://localhost:8001`。

---

## 3. 生产构建

先构建前端（输出到后端 `wwwroot`，由后端托管 SPA）：

```bash
cd frontend
npm install
npm run build
```

然后运行后端，后端会直接托管已构建的前端静态资源：

```bash
# 设置管理员密码（首次启动必填）
export FLOWENGINE_ADMIN_PASSWORD="your-strong-password-12+"

dotnet run --project backend/FlowEngine.Host
```

访问 `http://localhost:8001` 即可使用完整应用（前端 SPA 回退至 `index.html`）。

---

## 4. 测试

```bash
# 后端测试（xUnit v3）
dotnet test

# 前端测试（Vitest）
cd frontend && npm test
```

## 备注

- `plugins/` 由后端启动时扫描并注册（独立 `AssemblyLoadContext`），加载失败仅记警告；具体路径以后端启动日志为准。

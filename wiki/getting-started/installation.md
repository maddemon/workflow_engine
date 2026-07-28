# 安装与环境要求

本页说明运行 Flow Engine 所需的前置条件与获取代码的方式。架构与模块关系请参见 [系统总览](architecture/overview.md)。

## 1. 运行环境

| 组件 | 要求 | 说明 |
|------|------|------|
| .NET SDK | **.NET 10 SDK** | `FlowEngine.Core.csproj` 的 `TargetFramework` 为 `net10.0`，故需 .NET 10 SDK（仓库无 `global.json`，不锁定特定 SDK 补丁版本）。 |
| Node.js | **Node.js 20+** | 前端基于 Vite 8 / React 19 / TypeScript 6，需 Node 20 及以上（`package.json` 未声明 `engines`，按依赖推断）。 |
| 包管理器 | npm（随 Node 提供） | 前端依赖安装与脚本运行使用 npm。 |
| 数据库 | SQLite（自动创建） | 首次启动后端会应用 EF Core 迁移并自动生成 SQLite 数据库文件，无需手动安装数据库服务。 |

> 如需对接 SQL Server / PostgreSQL / MySQL，可在配置中替换连接串，详见 [系统总览](architecture/overview.md)。

## 2. 获取代码

克隆本仓库到本地：

```bash
git clone <本仓库地址>
cd flow_engine
```

## 3. 首次启动说明

- **数据库自动创建**：后端首次启动时（`UseFlowEngineAsync` 内）会自动应用迁移，创建并初始化 SQLite 数据库；默认会在 `plugins/` 目录扫描并加载节点插件 DLL。
- **管理员密码（必填）**：首次启动会播种默认管理员账户（`admin@flowengine.local`），因此**必须**先提供管理员密码，否则启动会失败。可通过配置项或环境变量设置（密码至少 12 位）：

```bash
# 方式一：环境变量
export FLOWENGINE_ADMIN_PASSWORD="your-strong-password-12+"

# 方式二：配置文件（appsettings.json 的 Setup:AdminPassword）
```

- **MCP 端点**：后端在 `/mcp` 暴露 Model Context Protocol（Streamable HTTP）端点，需认证后访问，可用于以 MCP 客户端驱动工作流。

## 4. 下一步

环境就绪后，前往 [快速上手](quick-start.md) 运行后端、前端开发模式或生产构建。

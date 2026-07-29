# Flow Engine

一个节点可**热插拔**的工作流自动化引擎。前端负责可视化编排，构建后由后端统一托管；后端确定性执行工作流，节点通过 DLL 插件扩展。默认以单机后台服务形态运行，并预留清晰的横向扩展路径。

[English documentation](README.md)

## 特性

- **可视化工作流编辑器** —— 画布上拖拽节点、连线、撤销/重做，参数在自动生成的面板中配置。
- **热插拔节点** —— 把节点编译成 DLL 放入 `plugins/`，启动时通过隔离的 `AssemblyLoadContext` 自动发现并注册。
- **确定性执行引擎** —— 按 DAG 拓扑顺序执行节点，支持多输入等待屏障、分支、重试、取消与 Saga 补偿。
- **安全的表达式引擎** —— 表达式为 JavaScript（经 Jint 求值），运行在受限沙箱中，使用 `$` 前缀变量（如 `$json`、`$input`、`$credentials`）。
- **凭据系统** —— 凭据静态加密存储（AES-GCM），运行时解密注入；明文不落日志、不返回前端。
- **触发器** —— 定时（Quartz.NET）、Webhook、轮询触发器均可启动执行。
- **实时执行视图** —— 执行事件经 WebSocket 推送，前端实时高亮展示。
- **AI Agent 层** —— 编排 LLM 调用、工具（含作为工具暴露的子工作流）与子 Agent。
- **AI 原生编排** —— 提供 MCP 技能，让 AI Agent 通过工具调用发现节点并装配、修改、校验、确认、执行工作流。
- **可插拔持久化** —— 默认 SQLite（零配置），支持 PostgreSQL / MySQL / SQL Server。

## 架构

```
Flow Engine 服务进程
┌──────────────────────────────────────────────────────────┐
│  前端静态资源 (wwwroot)                                      │
│   画布编辑器 · 节点面板 · 参数面板 · 执行视图                 │
├──────────────────────────────────────────────────────────┤
│  核心层                                                      │
│   执行引擎 · 表达式求值 · 节点注册中心                        │
├──────────────────────────────────────────────────────────┤
│  基础设施层                                                  │
│   凭据加密 · 审计日志(NDJSON) · Quartz 调度器 · 事件总线 · 文件存储 │
├──────────────────────────────────────────────────────────┤
│  扩展层（当前单机可承载，多机就绪）                           │
│   RBAC · SSO · MCP · Git 版本控制 · AI Builder              │
└──────────────────────────────────────────────────────────┘
```

前端只负责**描述工作流**与**展示执行过程**，所有执行逻辑都在后端。节点注册中心启动时扫描 `plugins/` 目录，将节点类型元数据发布给前端（`GET /api/node-types`），前端据此自动渲染节点面板与参数表单。

## 技术栈

| 层   | 技术                                                                                  |
| ---- | ------------------------------------------------------------------------------------- |
| 后端 | .NET 10 (C# 12)、ASP.NET Core、Entity Framework Core、Quartz.NET、Jint                |
| 前端 | React 19、TypeScript（严格模式）、Vite、Mantine、React Flow (`@xyflow/react`)、ahooks |
| 测试 | xUnit v3（后端）、Vitest（前端）                                                      |
| 存储 | SQLite（默认）· PostgreSQL / MySQL / SQL Server（扩展时）                             |

## 目录结构

```
FlowEngine.sln
├── backend/
│   ├── FlowEngine.Core/          # 实体、抽象契约、值对象，及下沉的脚本/HTTP/Agent/Tools 类型
│   ├── FlowEngine.Runtime/       # 执行引擎、表达式沙箱、等待区、快照
│   ├── FlowEngine.Application/   # 用例编排：工作流、执行、DTO、AI 编排器
│   ├── FlowEngine.Infrastructure/# 持久化、调度、事件总线、凭据加密、文件存储
│   ├── FlowEngine.Migrations/    # EF Core 迁移程序集
│   └── FlowEngine.Host/          # 组合根：Controllers、WebSocket、Middlewares、wwwroot
├── plugins/
│   └── FlowEngine.Plugins.Standard/  # 内置标准节点（HTTP、Code、If、Loop、Merge、Agent、LLM、DB 等）
├── frontend/                     # React + TypeScript 应用（构建产物输出至 backend/FlowEngine.Host/wwwroot）
├── tests/                        # xUnit 测试工程（Core/Application/Runtime/Host 等）
└── mcp-shim/                     # 通过 stdio/HTTP 暴露 AI Agent 技能的 MCP 垫片
```

**依赖方向**：`Host → Application → Runtime → Core`，且 `Plugins → Core` 单向（插件禁止引用 `Application` 或 `Runtime`）。

## 环境要求

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Node.js](https://nodejs.org/) 20 或更高版本

## 快速上手

### 运行后端（API + SPA 宿主）

```bash
dotnet run --project backend/FlowEngine.Host
```

服务启动于 `http://localhost:8001`（HTTPS 为 `https://localhost:8002`）。首次启动会创建 SQLite 数据库并扫描 `plugins/` 中的节点类型。MCP 端点托管在 `/mcp`。

### 开发模式运行前端

另开一个终端：

```bash
cd frontend
npm install
npm run dev
```

Vite 开发服务器运行于 `http://localhost:4000`，并将 `/api` 代理到后端的 `http://localhost:8001`。打开 `http://localhost:4000` 即可使用编辑器。

### 生产构建（由后端托管 SPA）

```bash
cd frontend
npm install
npm run build      # 输出到 backend/FlowEngine.Host/wwwroot
dotnet run --project backend/FlowEngine.Host
```

随后打开 `http://localhost:8001` —— 后端通过 `UseStaticFiles` + `MapFallbackToFile("index.html")` 托管构建后的 SPA。

## 创建 / 修改 / 测试工作流

可以通过两种方式构建与操作工作流。

### 通过 AI Agent IDE（MCP）

Flow Engine 内置一个 MCP 技能，使 AI Agent（Claude Code、Cursor、VS Code、Claude Desktop）能够通过工具调用发现节点并装配、修改、校验、确认、执行工作流 —— 无需手写完整 DSL。

**1. 从 UI 复制 MCP 配置。** 启动服务并登录 Web 客户端，打开 **帮助与 MCP 配置** 页面 —— 其中展示了完整、可直接使用的 MCP 服务器配置，API Key 已自动填好。

**2. 把配置交给 Agent。** 复制该配置并交给你的 Agent（粘贴到它的 MCP 设置中，或直接作为消息发给它 —— Agent 会自行注册该服务器）。无需手动修改，UI 提供的配置已包含正确的地址与 Key。

配置有两种形态（UI 会展示适配你客户端的那一种）：

- **HTTP（Claude Code / Cursor / VS Code）** —— Cursor 放入 `.cursor/mcp.json`；VS Code 使用 `.vscode/mcp.json`，但根键改为 `servers`：

  ```json
  {
    "mcpServers": {
      "flowengine": {
        "url": "http://localhost:8001/mcp",
        "headers": { "Authorization": "Bearer <apiKey>" }
      }
    }
  }
  ```

  （Claude Code 也可执行 `claude mcp add --transport http flowengine http://localhost:8001/mcp`，但需确保附加同样的 `Authorization` 请求头。）

- **stdio（Claude Desktop）** —— 注册自带的 MCP 垫片（`mcp-shim/`）作为 `stdio` 服务器：

  ```json
  {
    "mcpServers": {
      "flowengine": {
        "command": "node",
        "args": ["path/to/mcp-shim/dist/index.js"],
        "env": {
          "FLOWENGINE_URL": "http://localhost:8001",
          "FLOWENGINE_API_KEY": "<apiKey>"
        }
      }
    }
  }
  ```

HTTP 模式下 Key 以 `Authorization: Bearer <apiKey>` 请求头发送；stdio 模式下通过 `FLOWENGINE_API_KEY` 环境变量传递。Key 由 UI 签发，用于 Agent 对接 MCP 端点的认证，仅存在于客户端配置中，绝不写入工作流定义。

该技能暴露以下工具：

| 工具                              | 用途                                             |
| --------------------------------- | ------------------------------------------------ |
| `list_node_catalog`               | 列出可用节点类型（参数与端口）。                 |
| `get_node_detail`                 | 获取某节点类型的完整 schema。                    |
| `assemble_workflow`               | 根据自然语言或结构化描述创建工作流。             |
| `modify_workflow`                 | 修改已有工作流（增删节点、重连连线、编辑参数）。 |
| `validate_workflow`               | 校验结构、节点类型、端口、连线与必填参数。       |
| `confirm_workflow`                | 将校验通过的草稿保存为带版本的工作流。           |
| `execute_workflow`                | 执行工作流并查看结果。                           |
| `get_workflow` / `list_workflows` | 查看已有工作流。                                 |

典型闭环：描述需求 → Agent 列出节点并装配草稿 → `validate_workflow` → 修正报告的问题 → `confirm_workflow` 保存 → `execute_workflow` 测试。凭据仅以 ID 引用，密钥绝不写入工作流定义。

### 通过 Web 客户端（UI）

打开编辑器（见「快速上手」），以可视化方式构建工作流：

- 从节点面板拖拽节点到画布，并连接它们的端口。
- 在自动生成的参数面板中配置参数（条件字段与必填校验生效）。
- 点击 **运行** 手动执行，通过 WebSocket 实时高亮各节点并展示输出。
- 已保存的工作流后续可由定时、Webhook 或轮询触发器启动。

## 编写一个节点插件

节点是一个继承 `NodeBase` 的类，使用 `[NodeMeta]` / `[Port]` / 参数特性修饰，编译为 DLL 放入 `plugins/`。插件只引用 `FlowEngine.Core`。

```csharp
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Attributes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;

namespace FlowEngine.Plugins.Standard;

[NodeMeta(TypeName = "upper", DisplayName = "Uppercase", Category = NodeCategory.String, Icon = "text")]
[Port(FlowConstants.PortNames.Input, "Input", PortDirection.Input)]
[Port(FlowConstants.PortNames.Output, "Output", PortDirection.Output)]
public sealed class UppercaseNode : NodeBase
{
    [Description("待转换的文本。")]
    public string Text { get; set; } = string.Empty;

    public override async Task<NodeHandlerOutput> ExecuteAsync(NodeInput input, CancellationToken ct)
    {
        var result = Text.ToUpperInvariant();
        return NodeHandlerOutput.ToPort(FlowConstants.PortNames.Output, result);
    }
}
```

构建后将 DLL 复制到 `plugins/` 并重启宿主 —— 该节点会自动出现在前端面板中。节点系统参考文档描述了完整的节点契约以及 `[Required]` / `[Hint]` 参数特性。

## 测试

```bash
# 后端（xUnit v3）
dotnet test

# 前端（Vitest）
cd frontend
npm test
```

---

> 采得百花成蜜后，为谁辛苦为谁甜？
>
> —— [唐] 罗隐《蜂》

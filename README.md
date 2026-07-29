# Flow Engine

A workflow automation engine with **hot-pluggable nodes**. The React/TypeScript frontend provides visual orchestration and is served by the backend; the .NET backend executes workflows deterministically, and nodes are extended through DLL plugins loaded at startup. Runs as a single self-hosted service by default, with a clear path to horizontal scaling.

[中文文档](README.zh-CN.md)

## Features

- **Visual workflow editor** — drag-and-drop nodes on a canvas, wire them up, undo/redo, configure parameters in an auto-generated panel.
- **Hot-pluggable nodes** — implement a node as a DLL, drop it into `plugins/`, and it is discovered and registered on startup via an isolated `AssemblyLoadContext`.
- **Deterministic execution engine** — executes nodes in DAG topological order with multi-input barriers, branching, retries, cancellation, and Saga compensation.
- **Safe expression engine** — expressions are JavaScript (via Jint) evaluated in a restricted sandbox, using `$`-prefixed variables such as `$json`, `$input`, and `$credentials`.
- **Credentials system** — credentials are encrypted at rest (AES-GCM) and decrypted/injected at runtime; raw values never reach logs or the frontend.
- **Triggers** — schedule (Quartz.NET), webhook, and polling triggers start executions.
- **Real-time execution view** — execution events are pushed over WebSocket and rendered live in the frontend.
- **AI Agent layer** — orchestrate LLM calls, tools (including sub-workflows exposed as tools), and sub-agents.
- **AI-native authoring** — an MCP skill lets AI agents discover nodes and assemble, modify, validate, confirm, and execute workflows through tool calls.
- **Pluggable persistence** — SQLite by default (zero-config), with PostgreSQL / MySQL / SQL Server support.

## Architecture

```
Flow Engine service process
┌──────────────────────────────────────────────────────────┐
│  Frontend static assets (wwwroot)                          │
│   Canvas editor · Node panel · Parameter panel · Run view  │
├──────────────────────────────────────────────────────────┤
│  Core layer                                                 │
│   Execution engine · Expression evaluator · Node registry  │
├──────────────────────────────────────────────────────────┤
│  Infrastructure layer                                       │
│   Credential encryption · Audit log (NDJSON) · Quartz scheduler│
│   · Event bus · File storage                                │
├──────────────────────────────────────────────────────────┤
│  Extensibility layer (single-machine today, multi-machine ready)│
│   RBAC · SSO · MCP · Git versioning · AI Builder            │
└──────────────────────────────────────────────────────────┘
```

The frontend only **describes workflows** and **shows execution progress**; all execution logic lives in the backend. The node registry scans the `plugins/` directory at startup and publishes node-type metadata to the frontend (`GET /api/node-types`), which then renders the node panel and parameter form automatically.

## Tech Stack

| Layer    | Technology                                                                         |
| -------- | ---------------------------------------------------------------------------------- |
| Backend  | .NET 10 (C# 12), ASP.NET Core, Entity Framework Core, Quartz.NET, Jint             |
| Frontend | React 19, TypeScript (strict), Vite, Mantine, React Flow (`@xyflow/react`), ahooks |
| Tests    | xUnit v3 (backend), Vitest (frontend)                                              |
| Storage  | SQLite (default) · PostgreSQL / MySQL / SQL Server (scaling)                       |

## Project Structure

```
FlowEngine.sln
├── backend/
│   ├── FlowEngine.Core/          # Entities, abstractions, value objects, scripting/HTTP/Agent/Tools types
│   ├── FlowEngine.Runtime/       # Execution engine, expression sandbox, waiting area, snapshots
│   ├── FlowEngine.Application/   # Use-case orchestration: workflows, executions, DTOs, AI orchestrators
│   ├── FlowEngine.Infrastructure/# Persistence, scheduling, event bus, credential encryption, file storage
│   ├── FlowEngine.Migrations/    # EF Core migrations assembly
│   └── FlowEngine.Host/          # Composition root: Controllers, WebSocket, Middlewares, wwwroot
├── plugins/
│   └── FlowEngine.Plugins.Standard/  # Built-in nodes (HTTP, Code, If, Loop, Merge, Agent, LLM, DB, …)
├── frontend/                     # React + TypeScript app (build output → backend/FlowEngine.Host/wwwroot)
├── tests/                        # xUnit test projects (Core/Application/Runtime/Host/…)
└── mcp-shim/                     # MCP shim that exposes the AI Agent skill over stdio/HTTP
```

**Dependency direction:** `Host → Application → Runtime → Core`, and `Plugins → Core` only (plugins must never reference `Application` or `Runtime`).

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Node.js](https://nodejs.org/) 20 or later

## Quick Start

### Run the backend (API + SPA host)

```bash
dotnet run --project backend/FlowEngine.Host
```

The service starts on `http://localhost:8001` (HTTPS on `https://localhost:8002`). On first launch it creates the SQLite database and scans `plugins/` for node types. The MCP endpoint is served at `/mcp`.

### Run the frontend in development mode

In a second terminal:

```bash
cd frontend
npm install
npm run dev
```

The Vite dev server runs on `http://localhost:4000` and proxies `/api` to the backend at `http://localhost:8001`. Open `http://localhost:4000` to use the editor.

### Production build (backend serves the SPA)

```bash
cd frontend
npm install
npm run build      # outputs to backend/FlowEngine.Host/wwwroot
dotnet run --project backend/FlowEngine.Host
```

Then open `http://localhost:8001` — the backend serves the built SPA via `UseStaticFiles` + `MapFallbackToFile("index.html")`.

## Creating, Modifying, and Testing Workflows

You can build and operate workflows in two ways.

### Via the AI Agent IDE (MCP)

Flow Engine ships an MCP skill so AI agents (Claude Code, Cursor, VS Code, Claude Desktop) can discover nodes and assemble, modify, validate, confirm, and execute workflows through tool calls — without hand-writing the full DSL.

**1. Copy the MCP config from the UI.** Start the service and log in to the web client. Open **Help & MCP Configuration** — it shows the complete, ready-to-use MCP server config with your API key already filled in.

**2. Hand the config to your agent.** Copy that config and give it to your agent (paste it into its MCP settings, or just send it as a message — the agent registers the server itself). No manual editing is needed; the UI-provided config already contains the correct address and key.

The config comes in two shapes (the UI shows the one matching your client):

- **HTTP (Claude Code / Cursor / VS Code)** — for Cursor put this in `.cursor/mcp.json`; for VS Code use `.vscode/mcp.json` but change the root key to `servers`:

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

  (Claude Code alternatively: `claude mcp add --transport http flowengine http://localhost:8001/mcp`, then ensure the same `Authorization` header is attached.)

- **stdio (Claude Desktop)** — register the bundled MCP shim (`mcp-shim/`) as a `stdio` server:

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

In HTTP mode the key is sent as the `Authorization: Bearer <apiKey>` header; in stdio mode it is passed via the `FLOWENGINE_API_KEY` environment variable. The key authenticates the agent against the MCP endpoint, is issued by the UI, and lives only in the client config — it is never embedded in a workflow.

The skill exposes these tools:

| Tool                              | Purpose                                                               |
| --------------------------------- | --------------------------------------------------------------------- |
| `list_node_catalog`               | List available node types (parameters and ports).                     |
| `get_node_detail`                 | Get the full schema of a specific node type.                          |
| `assemble_workflow`               | Create a workflow from a natural-language or structured prompt.       |
| `modify_workflow`                 | Change an existing workflow (add/remove nodes, rewire, edit params).  |
| `validate_workflow`               | Check structure, node types, ports, connections, and required params. |
| `confirm_workflow`                | Save a validated draft as a versioned workflow.                       |
| `execute_workflow`                | Run a workflow and observe the result.                                |
| `get_workflow` / `list_workflows` | Inspect existing workflows.                                           |

Typical loop: describe what you want → the agent lists nodes and assembles a draft → `validate_workflow` → fix any reported issues → `confirm_workflow` to save → `execute_workflow` to test. Credentials are referenced by ID only; secrets are never embedded in the workflow definition.

### Via the Web Client (UI)

Open the editor (see Quick Start) and build workflows visually:

- Drag nodes from the node panel onto the canvas and wire their ports together.
- Configure parameters in the auto-generated panel (conditional fields and required-field validation apply).
- Click **Run** to execute manually and watch nodes highlight with live output over WebSocket.
- Saved workflows can later be triggered by schedule, webhook, or polling triggers.

## Writing a Node Plugin

A node is a class that inherits `NodeBase`, decorated with `[NodeMeta]` / `[Port]` / parameter attributes, and compiled into a DLL placed in `plugins/`. Plugins reference only `FlowEngine.Core`.

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
    [Description("Text to transform.")]
    public string Text { get; set; } = string.Empty;

    public override async Task<NodeHandlerOutput> ExecuteAsync(NodeInput input, CancellationToken ct)
    {
        var result = Text.ToUpperInvariant();
        return NodeHandlerOutput.ToPort(FlowConstants.PortNames.Output, result);
    }
}
```

After building, copy the DLL into `plugins/` and restart the host — the node appears automatically in the frontend panel. The node system reference describes the full node contract and the `[Required]` / `[Hint]` parameter attributes.

## Testing

```bash
# Backend (xUnit v3)
dotnet test

# Frontend (Vitest)
cd frontend
npm test
```

---

> 采得百花成蜜后，为谁辛苦为谁甜？
> _— after gathering a hundred flowers into honey, for whom the toil, for whom the sweet?_
>
> —— [唐] 罗隐《蜂》

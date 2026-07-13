# MCP AI 体验改进计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use subagent-driven-development (recommended) or executing-plans to implement this plan task-by-task.

**Goal:** 解决 AI **仅通过 MCP（不读源码）** 装配工作流时，因 n8n 心智模型未被契约消解、且执行前无廉价校验，导致的反复试错。所有改进都必须落在 AI 看得到的 MCP 输出上（catalog / get_node_detail / validate_workflow）。

**Architecture:** 六个独立改进，按收益排序——
(0) 新增全局表达式约定声明，从根上消解 n8n 假设；
(1) modify_workflow 支持基于草稿迭代；
(2) Schema 增加凭据字段映射与凭据类型说明；
(3) Schema 增加 expressionLanguage / antiPatterns / examples / 输出访问提示；
(4) validate_workflow 增加 mustache 词法扫描（廉价、免凭据，抓 `{{ }}`）；
(5) 运行时 Script 错误暴露到节点记录（次级安全网）。

**Tech Stack:** C# 12, ASP.NET Core, Jint (JavaScript 引擎), ModelContextProtocol.AspNetCore

**前置知识 (AI Agent 必读):**
- `docs/architecture/node-system.md` — 节点参数系统
- `backend/FlowEngine.Core/Ai/NodeDefinitionAdapter.cs` — Schema 构建逻辑（MCP `get_node_detail` 的数据源）
- `backend/FlowEngine.Core/Ai/AiNodeDefinition.cs` — AI 节点定义模型（MCP 暴露的契约）
- `backend/FlowEngine.Application/Workflows/WorkflowDraftValidator.cs` — `validate_workflow` 的校验逻辑
- `backend/FlowEngine.Host/Mcp/Tools/CatalogTools.cs` / `WorkflowLifecycleTools.cs` — MCP 工具入口

## 全局约束

- 不改变现有 API 契约的向后兼容性（增字段、增工具不删字段）。
- AI **只通过 MCP** 看到 schema/catalog/validate 输出，不读源码；所有修复必须增强 MCP 暴露的契约。
- 校验必须是**廉价、免凭据、不执行**的（在 `validate_workflow` 内完成），让 AI 有 `validate → fix` 闭环，而不是等 `execute_workflow` 拿真实凭据跑才翻车。
- 本引擎表达式是 **JavaScript（Jint）**，明确不支持 n8n 的 `{{ }}` mustache 模板。
- **关键机制纠正**：`{{ }}` 是否会被 JS 校验抓到，取决于**有没有引号**：
  - 裸写 `https://x?t={{$json.token}}`（无引号）：引擎把它当 JS 表达式原样求值（见 `HttpNodeExecution` → `ScriptEvaluationExtensions` → `ScriptCompiler.Compile`），`//` 被当成行注释，剩 `https:` 是不完整的 label 语句 → Acornima/Jint 抛 **ParseError**，`ScriptCache` 记录 `CompileError`。即**JS 语法校验能抓到这种写法**（但报错信息晦涩，不如 n8n 提示直指要害）。
  - 带引号 `'https://x?t={{$json.token}}'`：对 Jint 是**合法 JS 字符串字面量**，编译通过、`{{}}` 原样保留 → JS 校验**漏报**。
  - 因此 **`validate_workflow` 必须同时做两件事**：① 词法扫描命中 `{{` / `}}`（首要防线，对带/不带引号都生效，给出"本引擎不支持 n8n 模板"直白提示）；② 对表达式参数跑一次 `ScriptCompiler.Compile` 作为通用语法网（兜住裸写 `{{}}` 之外的其它语法错误，如括号不匹配）。
- `ValidationError.ErrorType` 已有 `"InvalidExpression"` 枚举值，直接复用；`Message` 需给出节点 ID、参数名与正确写法。
- 所有修改必须配套测试。

---

### Task 0: 全局表达式约定声明（根因：消解 n8n 心智模型）

**Files:**
- Add: `backend/FlowEngine.Host/Mcp/Tools/ConventionTools.cs` (新 MCP 工具 `get_conventions`)
- Modify: `backend/FlowEngine.Core/Ai/AiNodeDefinition.cs` (增 `ExpressionLanguage` 字段)
- Modify: `backend/FlowEngine.Core/Ai/NodeDefinitionAdapter.cs` (ToAiDefinition 设 `ExpressionLanguage`)
- Test: `tests/FlowEngine.Host.Tests/Mcp/ConventionToolsTests.cs`
- Test: `tests/FlowEngine.Core.Tests/Ai/NodeDefinitionAdapterTests.cs`

**Why（根因）:**
AI 只通过 MCP 看到节点定义。当前 `get_node_detail` 对 `url` 只标 `supportsExpression:true` + 一段 prose，全仓库没有任何地方写"这不是 n8n、不支持 `{{ }}`"。强 n8n 先验下，模型把"expression"理解成 `{{$json.x}}`，于是写出本次事故的根因代码。修复必须从"让 AI 第一眼就看到约定"入手——一句话声明胜过十段描述。

**方案:**
1. 新增 MCP 工具 `get_conventions`，返回全局约定对象（一次性进上下文，强制消解 n8n 假设）。
2. `AiNodeDefinition` 增加根级 `expressionLanguage` 字段（默认 `"javascript"`）。
3. `get_node_detail` 的描述强调"JavaScript 表达式，非 n8n 模板"。

- [ ] **Step 1: 写测试 — get_conventions 返回约定且明确否定 `{{ }}`**

```csharp
// tests/FlowEngine.Host.Tests/Mcp/ConventionToolsTests.cs
[Fact]
public void GetConventions_StatesJavaScriptAndNoMustache()
{
    var tools = new ConventionTools();
    var result = tools.GetConventions();

    Assert.Equal("javascript", result["expressionLanguage"]?.GetValue<string>());
    var summary = result["summary"]?.GetValue<string>() ?? "";
    Assert.Contains("JavaScript", summary);
    Assert.Contains("{{", summary);                 // 明确点名不支持 {{ }}
    Assert.Contains("mustache", summary.ToLowerInvariant()); // 明确点名不支持 n8n 模板

    var rules = result["rules"] as JsonArray;
    Assert.NotNull(rules);
    Assert.Contains(rules!, r => (r?.GetValue<string>() ?? "").Contains("{{")); // 规则里也给正确写法
}
```

- [ ] **Step 2: 确认测试失败** (工具/字段尚不存在)

Run: `dotnet test tests/FlowEngine.Host.Tests --filter "GetConventions_"`
Expected: FAIL

- [ ] **Step 3: 实现 get_conventions 工具**

```csharp
// backend/FlowEngine.Host/Mcp/Tools/ConventionTools.cs
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;

namespace FlowEngine.Host.Mcp.Tools;

[McpServerToolType]
public sealed class ConventionTools
{
    [McpServerTool(Name = "get_conventions")]
    [Description("返回 Flow Engine 的全局约定，尤其是表达式语言。AI 在组装工作流前应优先阅读，以消解 n8n 等其它工具的心智模型差异。")]
    public JsonNode GetConventions()
    {
        return new JsonObject
        {
            ["expressionLanguage"] = "javascript",
            ["summary"] =
                "本引擎的 Script/Expression 参数是 JavaScript 表达式（Jint），使用 $json（当前 item 数据）和 " +
                "$input（输入容器）。不支持 n8n 的 {{ }} mustache 模板，也不要使用其它模板语法。",
            ["rules"] = new JsonArray
            {
                "'https://api.com/path?token=' + $json.token（不要写 {{$json.token}}）",
                "引用上游输出用 $json；多 item/数组用 $input.all() / $input.first()",
                "HTTP 节点响应被包成 { statusCode, headers, body }，下游用 $input.first().body.x 取业务字段",
                "字符串拼接用 + 与单/双引号；禁止 {{ }} 模板",
            },
        };
    }
}
```

- [ ] **Step 4: 给 AiNodeDefinition 增加 ExpressionLanguage 字段并在适配器中赋值**

```csharp
// backend/FlowEngine.Core/Ai/AiNodeDefinition.cs 内新增
/// <summary>表达式语言，固定为 "javascript"。用于在 AI 定义中显式声明，消解 n8n 模板假设。</summary>
public string ExpressionLanguage { get; set; } = "javascript";
```

```csharp
// backend/FlowEngine.Core/Ai/NodeDefinitionAdapter.cs，ToAiDefinition 中
var def = new AiNodeDefinition
{
    // ... 现有字段 ...
    ExpressionLanguage = "javascript",
};
```

- [ ] **Step 5: 运行测试确认通过**

Run: `dotnet test tests/FlowEngine.Host.Tests --filter "GetConventions_"`
Expected: PASS

- [ ] **Step 6: 提交**

```bash
git add backend/FlowEngine.Host/Mcp/Tools/ConventionTools.cs backend/FlowEngine.Core/Ai/AiNodeDefinition.cs backend/FlowEngine.Core/Ai/NodeDefinitionAdapter.cs tests/FlowEngine.Host.Tests/Mcp/ConventionToolsTests.cs tests/FlowEngine.Core.Tests/Ai/NodeDefinitionAdapterTests.cs
git commit -m "feat(mcp): add get_conventions and expressionLanguage to disambiguate from n8n"
```

---

### Task 1: modify_workflow 支持基于最新草稿迭代（减少冗余记录）

**Files:**
- Modify: `backend/FlowEngine.Application/Workflows/WorkflowModificationService.cs:32-138` (ModifyAsync)
- Modify: `backend/FlowEngine.Application/Dtos/WorkflowAssemblyDtos.cs:97-103` (ModifyWorkflowRequest 加 DraftId)
- Modify: `backend/FlowEngine.Host/Mcp/Tools/WorkflowTools.cs:92-143` (ModifyWorkflow 加 draftId 参数)
- Test: `tests/FlowEngine.Application.Tests/Workflows/WorkflowModificationServiceTests.cs`

**问题:**
每次 `modify_workflow` 都从原始活跃工作流深拷贝创建新草稿。AI 多次修改会生成 N 个无继承关系的独立草稿。AI 无法"在上次修改的基础上继续修改"。

**陷阱（MCP 视角）:** 若 AI 再次调用时忘了传 `draftId`，会回退到"最新活跃版本"（原始工作流），丢失之前所有迭代。MCP 工具描述必须明确要求"每次都传上一次返回的最新 draftId"。

**方案:**
`modify_workflow` 加可选参数 `draftId`。传入时以该草稿为源做深拷贝 + 修改；不传时保持当前行为（以最新活跃版本为源）。

- [ ] **Step 1: 写测试 — ModifyAsync 带 draftId 时基于草稿修改**

```csharp
[Fact]
public async Task ModifyAsync_WithDraftId_ModifiesFromDraft()
{
    // 1. 创建原始工作流
    // 2. 创建一次修改 → 得到 draftA
    // 3. 以 draftA.Id 为源再次修改 → 应基于 draftA 的内容，不是原始工作流
    // 验证：第二次修改的结果包含第一次修改的内容
}
```

- [ ] **Step 2: 确认测试失败**

Run: `dotnet test tests/FlowEngine.Application.Tests --filter "ModifyAsync_WithDraftId_ModifiesFromDraft"`
Expected: FAIL

- [ ] **Step 3: 修改 ModifyWorkflowRequest 增加 DraftId**

```csharp
// WorkflowAssemblyDtos.cs
public sealed record ModifyWorkflowRequest
{
    public List<WorkflowOperation> Operations { get; init; } = [];

    /// <summary>
    /// 可选。指定基于哪个草稿版本修改。不传时以最新活跃版本为源。
    /// AI 必须始终传入上一次 modify_workflow 返回的最新 draftId，否则会丢失之前的迭代。
    /// </summary>
    public Guid? DraftId { get; init; }
}
```

- [ ] **Step 4: 修改 MCP 工具签名（描述强调 draftId 连续性）**

```csharp
// WorkflowTools.cs ModifyWorkflow 方法
[McpServerTool(Name = "modify_workflow")]
public async Task<object> ModifyWorkflow(
    [Description("源工作流 ID。")] string workflowId,
    [Description("修改操作列表。")] List<WorkflowOperation> operations,
    [Description("可选但强烈建议：上一次 modify_workflow 返回的最新 draftId。每次迭代都必须传入，否则会基于原始工作流重来、丢失之前修改。")] string? draftId = null,
    CancellationToken cancellationToken = default)
```

- [ ] **Step 5: 修改 ModifyAsync 核心逻辑**

```csharp
// WorkflowModificationService.cs ModifyAsync
public async Task<ModifyWorkflowResult> ModifyAsync(
    Guid workflowId,
    ModifyWorkflowRequest request,
    CancellationToken cancellationToken = default)
{
    // 如果有 DraftId，以草稿为源；否则以活跃版本为源
    Guid sourceId = request.DraftId ?? workflowId;

    var existing = await dbContext.Workflows
        .AsNoTracking()
        .FirstOrDefaultAsync(w => w.Id == sourceId, cancellationToken)
        .ConfigureAwait(false);
    // ... 其余逻辑不变 ...
}
```

- [ ] **Step 6: 运行测试确认通过**

Run: `dotnet test tests/FlowEngine.Application.Tests --filter "ModifyAsync_WithDraftId_ModifiesFromDraft"`
Expected: PASS

- [ ] **Step 7: 提交**

```bash
git add backend/FlowEngine.Application/Workflows/WorkflowModificationService.cs backend/FlowEngine.Application/Dtos/WorkflowAssemblyDtos.cs backend/FlowEngine.Host/Mcp/Tools/WorkflowTools.cs tests/FlowEngine.Application.Tests/Workflows/WorkflowModificationServiceTests.cs
git commit -m "feat(mcp): modify_workflow supports draftId for iterative editing"
```

---

### Task 2: Schema 增加凭据字段映射与凭据类型说明

**Files:**
- Modify: `backend/FlowEngine.Core/Ai/NodeDefinitionAdapter.cs` (BuildInputSchema 增加 authFieldMapping 与 credentialType)
- Test: `tests/FlowEngine.Core.Tests/Ai/NodeDefinitionAdapterTests.cs`

**问题:**
AI 不知道 `httpRequest` + `QueryParameter` 模式读的是 `accessToken`/`token`/`apiKey` 字段，而钉钉 OAuth2 凭据存的是 `clientId`/`clientSecret`；也不知道 `dbUpsert` 的 `connection` 必须是 **type=`database` 的凭据 ID**（本次事故中它填了占位符 `YOUR_DB_CREDENTIAL_ID`）。`get_node_detail` 没有说明各认证模式需要哪些凭据字段、也没暴露凭据类型。

**方案:**
1. 在 `inputSchema` 的认证模式参数中增加 `credentialFieldMapping`，描述各模式使用的凭据字段。
2. 在 `Credential` 类型参数中暴露 `credentialType`（取自 `[Credential("database")]` 特性），让 AI 知道要传对应类型的凭据 ID。

- [ ] **Step 1: 写测试 — 认证参数有字段映射、凭据参数有类型**

```csharp
[Fact]
public void BuildInputSchema_AuthParameter_HasCredentialFieldMapping_And_CredentialType()
{
    var authParam = new ParameterDefinition
    {
        Name = "authentication",
        Type = ParameterType.Options,
        Options =
        [
            new() { Value = "None" },
            new() { Value = "BearerToken" },
            new() { Value = "QueryParameter" },
            new() { Value = "ApiKey" },
            new() { Value = "BasicAuth" },
        ]
    };
    // connection 参数由 [Credential("database")] 推导类型
    var connParam = new ParameterDefinition
    {
        Name = "connection",
        Type = ParameterType.Credential,
        CredentialType = "database",
    };
    var descriptor = new NodeTypeDescriptor
    {
        TypeName = "dbUpsert",
        Parameters = [authParam, connParam],
        Ports = []
    };

    var schema = NodeDefinitionAdapter.BuildInputSchema(descriptor);
    var authProp = schema["properties"]!["authentication"]!;
    Assert.NotNull(authProp["credentialFieldMapping"]);

    var connProp = schema["properties"]!["connection"]!;
    Assert.Equal("database", connProp["credentialType"]?.GetValue<string>());
}
```

- [ ] **Step 2: 确认测试失败**

Run: `dotnet test tests/FlowEngine.Core.Tests/Ai --filter "BuildInputSchema_AuthParameter_HasCredentialFieldMapping_And_CredentialType"`
Expected: FAIL

- [ ] **Step 3: 实现字段映射与凭据类型**

```csharp
// NodeDefinitionAdapter.cs BuildInputSchema 方法内
// 1) 认证模式字段映射
if (p.Options.Count > 0 && s_authFieldMappings.TryGetValue(p.Name, out var modeMap))
{
    var mapping = new JsonObject();
    foreach (var (mode, fields) in modeMap)
    {
        var arr = new JsonArray();
        foreach (var f in fields) arr.Add(f);
        mapping[mode] = arr;
    }
    propSchema["credentialFieldMapping"] = mapping;
}

// 2) 凭据类型（来自 ParameterDefinition.CredentialType）
if (p.Type == ParameterType.Credential && !string.IsNullOrEmpty(p.CredentialType))
{
    propSchema["credentialType"] = JsonValue.Create(p.CredentialType);
    propSchema["description"] = JsonValue.Create(
        (p.Description + $" 必须是类型为 '{p.CredentialType}' 的凭据 ID，不要填占位符。").Trim());
}
```

`s_authFieldMappings` 同原方案（BearerToken/QueryParameter → accessToken/token/apiKey；ApiKey → apiKey；BasicAuth → username/password）。

- [ ] **Step 4: 运行测试确认通过**

Run: `dotnet test tests/FlowEngine.Core.Tests/Ai --filter "BuildInputSchema_AuthParameter_HasCredentialFieldMapping_And_CredentialType"`
Expected: PASS

- [ ] **Step 5: 提交**

```bash
git add backend/FlowEngine.Core/Ai/NodeDefinitionAdapter.cs tests/FlowEngine.Core.Tests/Ai/NodeDefinitionAdapterTests.cs
git commit -m "feat(ai): expose credentialFieldMapping and credentialType in schema"
```

---

### Task 3: Schema 增加 expressionLanguage / antiPatterns / examples / 输出访问提示

**Files:**
- Modify: `backend/FlowEngine.Core/Ai/NodeDefinitionAdapter.cs` (BuildInputSchema + BuildOutputSchema)
- Modify: `backend/FlowEngine.Core/Ai/AiNodeDefinition.cs` (动态构建，无需改类本身即可附加字段)
- Test: `tests/FlowEngine.Core.Tests/Ai/NodeDefinitionAdapterTests.cs`

**问题（结合本次事故）:**
当前 `get_node_detail` 的 `inputSchema` 中，Script 类型参数只标 `"supportsExpression": true`，AI 不知道：
- 表达式是 JS 语法（不是 n8n 的 `{{ }}` 模板）——本次 `url` 误写 `{{$json.access_token}}`；
- 正确写法示例——本次 `transform` 误用 `$input.first().result.list`，而 httpRequest 输出被包成 `{statusCode, headers, body}`，应为 `$input.first().body.result.list`；
- 上游 token 如何拼进 URL——本次本应写 `'...?access_token=' + $json.body.access_token`。

模型对 `examples` 的权重远高于 prose，因此必须用**反模式 + 正确示例**直击误区。

**修正（重要）:** `ParameterType` 枚举**没有 `Expression` 值**（只有 String/Number/Boolean/Options/Json/Code/Credential/Resource/Array/File/Script）。原方案测试用 `ParameterType.Expression` 会编译失败。应改用 `ParameterType.Script`（配合 `PresentationHint.Expression`），其类型本就在 `IsExpressionType` 集合中。

- [ ] **Step 1: 写测试 — Script 参数带 expressionLanguage / antiPatterns / examples**

```csharp
// tests/FlowEngine.Core.Tests/Ai/NodeDefinitionAdapterTests.cs
[Fact]
public void BuildInputSchema_ScriptParameter_HasExpressionMeta()
{
    var param = new ParameterDefinition
    {
        Name = "url",
        Type = ParameterType.Script,            // 注意：无 ParameterType.Expression
        Hint = PresentationHint.Expression,
        Description = "Target URL"
    };
    var descriptor = new NodeTypeDescriptor
    {
        TypeName = "httpRequest",
        Parameters = [param],
        Ports = []
    };

    var schema = NodeDefinitionAdapter.BuildInputSchema(descriptor);
    var urlProp = schema["properties"]!["url"]!;

    Assert.Equal("javascript", urlProp["expressionLanguage"]?.GetValue<string>());
    Assert.NotNull(urlProp["antiPatterns"]);   // 含 {{ }} 反例
    Assert.NotNull(urlProp["examples"]);       // 含正确拼接示例
}
```

- [ ] **Step 2: 确认测试失败**

Run: `dotnet test tests/FlowEngine.Core.Tests/Ai --filter "BuildInputSchema_ScriptParameter_HasExpressionMeta"`
Expected: FAIL（字段尚不存在）

- [ ] **Step 3: 修改 BuildInputSchema 增加 expressionLanguage / antiPatterns / examples**

```csharp
// NodeDefinitionAdapter.cs，BuildInputSchema 方法内，IsExpressionType(p.Type) 分支中
if (IsExpressionType(p.Type))
{
    propSchema["supportsExpression"] = true;
    propSchema["expressionLanguage"] = JsonValue.Create("javascript");

    var examples = GetExpressionExamples(p.Name);
    if (examples is not null) propSchema["examples"] = examples;

    var anti = GetAntiPatterns(p.Name);
    if (anti is not null) propSchema["antiPatterns"] = anti;
}
```

- [ ] **Step 4: 实现 GetExpressionExamples / GetAntiPatterns**

```csharp
private static JsonArray? GetExpressionExamples(string paramName) => paramName.ToLowerInvariant() switch
{
    "url" => new JsonArray
    {
        "'https://api.example.com/items'",                                       // 静态
        "'https://api.example.com/items/' + $json.id",                          // 拼接路径
        "'https://api.example.com/items?page=' + $json.page + '&size=100'",     // 查询参数
        "'https://oapi.dingtalk.com/topapi/v2/user/list?access_token=' + $json.body.access_token", // 上游 token 拼接
    },
    "bodyexpression" or "body_expression" or "body" => new JsonArray
    {
        "return { name: $json.name, count: $json.count };",
        "return { items: $input.all().map(i => i.data) };",
    },
    "headersexpression" or "headers_expression" or "headers" => new JsonArray
    {
        "return { 'Authorization': 'Bearer ' + $json.token };",
    },
    "successwhen" => new JsonArray
    {
        "$json.errcode == 0",
        "$json.status == 'ok'",
    },
    _ => null,
};

private static JsonArray? GetAntiPatterns(string paramName) => paramName.ToLowerInvariant() switch
{
    "url" => new JsonArray
    {
        JsonNode.Parse("""{"wrong":"https://x?t={{$json.token}}","why":"{{ }} 是 n8n mustache 模板，本引擎不支持；裸写会被 JS 解析为 '//' 注释导致编译报错，带引号则静默通过并错把 token 原样发出。务必用 JS 拼接","right":"'https://x?t=' + $json.body.token"}"""),
    },
    "bodyexpression" or "body_expression" or "body" => new JsonArray
    {
        JsonNode.Parse("""{"wrong":"return { token: {{$json.token}} }","why":"{{ }} 不是本引擎语法","right":"return { token: $json.token }"}"""),
    },
    "successwhen" => new JsonArray
    {
        JsonNode.Parse("""{"wrong":"{{$json.errcode}} == 0","why":"{{ }} 不是本引擎语法","right":"$json.errcode == 0"}"""),
    },
    _ => null,
};
```

- [ ] **Step 5: 输出访问提示（消解 `$input.first().result` 错误）**

在 `ToAiDefinition` 构建 `OutputSchema` 后，追加一条访问说明，让 AI 知道 HTTP 响应被包装：

```csharp
// NodeDefinitionAdapter.cs ToAiDefinition 内
var outputSchema = BuildOutputSchema(descriptor, overrideDef);
if (outputSchema is JsonObject outObj && !outObj.ContainsKey("description"))
{
    outObj["description"] = JsonValue.Create(
        "节点输出 data 的结构。例如 HTTP 节点响应被包成 { statusCode, headers, body }，" +
        "下游用 $input.first().body.x 取业务字段，而不是 $input.first().x 或 .result。");
}
def.OutputSchema = outputSchema;
```

- [ ] **Step 6: 运行测试确认通过**

Run: `dotnet test tests/FlowEngine.Core.Tests/Ai --filter "BuildInputSchema_ScriptParameter_HasExpressionMeta"`
Expected: PASS

- [ ] **Step 7: 提交**

```bash
git add backend/FlowEngine.Core/Ai/NodeDefinitionAdapter.cs tests/FlowEngine.Core.Tests/Ai/NodeDefinitionAdapterTests.cs
git commit -m "feat(ai): add expressionLanguage, antiPatterns, examples and output hints to schema"
```

---

### Task 4: validate_workflow 增加 mustache 词法扫描（抓 `{{ }}`）

**Files:**
- Modify: `backend/FlowEngine.Application/Workflows/WorkflowDraftValidator.cs` (ValidateAsync + 新增 CollectMustacheErrors)
- Test: `tests/FlowEngine.Application.Tests/Workflows/WorkflowDraftValidatorTests.cs`

**问题（关键机制纠正）:**
当前 `validate_workflow` 只校验节点结构/必填/连接，不对 Script 源码做语义检查。本次事故的 `url:"...?access_token={{$json.access_token}}"` 结构合法、通过了校验，到 `execute_workflow` 才暴露。

需要纠正一个常见误判：`{{$json.access_token}}` 这类写法**并非总是**能靠 JS 校验抓到：
- 裸写（无引号）`https://...{{...}}`：引擎直接把整段当 JS 表达式求值（见 `HttpNodeExecution` → `ScriptEvaluationExtensions` → `ScriptCompiler.Compile`）。其中 `//` 被 Acornima 当成行注释，剩 `https:` 是不完整的 label 语句，编译期抛 **ParseError**，`ScriptCache` 记录 `CompileError`。即**裸写 `{{}}` 会被 JS 校验抓到**（但报错信息晦涩）。
- 带引号 `'https://...{{...}}'`：对 Jint 是**合法 JS 字符串字面量**，编译通过、`{{}}` 原样保留 → JS 校验**漏报**。

**正确方案：`validate_workflow` 同时做两层防线**（都放在 `WorkflowDraftValidator`，免凭据、不执行）：**
1. **首要——词法扫描命中 `{{` / `}}`**（`CollectMustacheErrors`）：对带/不带引号的 `{{}}` 都生效，且给出直白的"本引擎不支持 n8n mustache 模板，请改 JS 拼接"提示，比 JS 编译的晦涩报错有用得多。
2. **通用网——对表达式参数跑一次 `ScriptCompiler.Compile`**：兜住裸写 `{{}}`（编译期 ParseError，作为次级提示）以及其它 JS 语法错误（括号不匹配、非法标识符等），让 AI 在 `execute` 之前就拿到反馈。

**Interfaces:**
- Consumes: 节点参数字典（递归扫描所有字符串叶子，含 `url`、`bodyExpression`、`columns` 等）
- Produces: `ValidationError`（`ErrorType = "InvalidExpression"`），Message 含节点 ID、字段名与正确写法

- [ ] **Step 1: 写测试 — mustache 被抓、合法 JS 通过**

```csharp
// tests/FlowEngine.Application.Tests/Workflows/WorkflowDraftValidatorTests.cs
[Fact]
public void CollectMustacheErrors_MustacheInUrl_Reported()
{
    var node = JsonNode.Parse("""{"id":"getEmployees","typeName":"httpRequest","parameters":{"url":"https://x?access_token={{$json.access_token}}"}}""")!;
    var errors = new List<string>();
    WorkflowDraftValidator.CollectMustacheErrors(node["parameters"], "getEmployees", errors);

    Assert.NotEmpty(errors);
    Assert.Contains(errors, e => e.Contains("{{") && e.Contains("url"));
}

[Fact]
public void CollectMustacheErrors_ValidJs_Passes()
{
    var node = JsonNode.Parse("""{"id":"getEmployees","typeName":"httpRequest","parameters":{"url":"'https://x?access_token=' + $json.body.access_token"}}""")!;
    var errors = new List<string>();
    WorkflowDraftValidator.CollectMustacheErrors(node["parameters"], "getEmployees", errors);

    Assert.Empty(errors);
}
```

- [ ] **Step 2: 确认测试失败**

Run: `dotnet test tests/FlowEngine.Application.Tests --filter "CollectMustacheErrors_"`
Expected: FAIL（方法不存在）

- [ ] **Step 3: 实现 CollectMustacheErrors（递归扫描字符串叶子）**

```csharp
// backend/FlowEngine.Application/Workflows/WorkflowDraftValidator.cs
/// <summary>
/// 递归扫描参数字典中的字符串叶子，命中 n8n mustache 标记 {{ / }} 即报错。
/// 注意：本引擎表达式是 JavaScript，不支持 n8n 的 {{ }} 模板。
/// 裸写 https://x?t={{...}} 会被 JS 引擎当成 "//" 注释导致编译失败（JS 校验可抓到但报错晦涩）；
/// 带引号 'https://x?t={{...}}' 则是合法字符串字面量、JS 校验漏报。
/// 因此词法扫描是首要防线（带/不带引号都命中），JS 编译校验作为通用语法网补充。
/// </summary>
public static void CollectMustacheErrors(JsonNode? parameters, string nodeId, List<string> errors)
{
    if (parameters is null) return;
    Scan(parameters, nodeId, errors, fieldName: null);
}

private static void Scan(JsonNode node, string nodeId, List<string> errors, string? fieldName)
{
    switch (node)
    {
        case JsonValue value:
            if (value.GetValueKind() == System.Text.Json.JsonValueKind.String)
            {
                var raw = value.GetValue<string>();
                if (raw.Contains("{{") || raw.Contains("}}"))
                {
                    var where = fieldName is null ? $"节点 \"{nodeId}\"" : $"节点 \"{nodeId}\" 参数 \"{fieldName}\"";
                    errors.Add(
                        $"{where} 含 n8n 风格的 {{ }} 模板语法，本引擎不支持。" +
                        $"请改用 JavaScript 表达式，例如：'https://api.com/path?token=' + $json.token");
                }
            }
            break;

        case JsonObject obj:
            foreach (var prop in obj)
            {
                Scan(prop.Value!, nodeId, errors, prop.Key);
            }
            break;

        case JsonArray arr:
            foreach (var item in arr) Scan(item!, nodeId, errors, fieldName);
            break;
    }
}
```

- [ ] **Step 3b: 增加 JS 编译检查（通用语法网）**

对节点的每个表达式参数（`IsExpressionType(p.Type)` 或 `PresentationHint.Expression`）源码跑一次编译，兜住裸写 `{{}}` 之外的语法错误。为复用 Core 现有编译产物，建议在 `ScriptCompiler` 新增一个 public 包装（或开放 `InternalsVisibleTo(FlowEngine.Application)`），避免直接依赖 `internal` 类型：

```csharp
// backend/FlowEngine.Core/Scripting/ScriptCompiler.cs（新增 public 包装）
public static bool TryCompile(Script script, out ScriptErrorException? error)
{
    try { Compile(script); error = null; return true; }
    catch (ScriptErrorException ex) { error = ex; return false; }
}

// backend/FlowEngine.Application/Workflows/WorkflowDraftValidator.cs
public static void CollectExpressionSyntaxErrors(JsonNode? parameters, string nodeId, List<string> errors)
{
    if (parameters is not JsonObject obj) return;
    foreach (var prop in obj)
    {
        switch (prop.Value)
        {
            case JsonValue v when v.GetValueKind() == JsonValueKind.String:
                var raw = v.GetValue<string>();
                if (string.IsNullOrWhiteSpace(raw)) continue;
                if (!ScriptCompiler.TryCompile(new Script { Source = raw, Language = ScriptLanguage.JavaScript }, out var err))
                    errors.Add($"节点 \"{nodeId}\" 参数 \"{prop.Key}\" 的 JS 表达式无法编译：{err!.Message}");
                break;
            case JsonObject or JsonArray:
                CollectExpressionSyntaxErrors(prop.Value, nodeId, errors); // 递归深入（注意大部分表达式在字符串叶子，按需即可）
                break;
        }
    }
}
```

> 落地要点：把命中的 `fieldName` 透传进 `ValidationError` 的 `Message` 与 `ErrorType = "InvalidExpression"`，让 AI 精确知道改哪个参数。
> - **首要防线是命中 `{{`/`}}` 即报错**（`CollectMustacheErrors`，带/不带引号都命中，提示最直白）。
> - **通用网是 `CollectExpressionSyntaxErrors`**（`ScriptCompiler.TryCompile`），兜住裸写 `{{}}` 之外的语法错误；裸写 `{{}}` 若被它先抓到也一并报告，但优先以 mustache 扫描的同字段直白提示为准。
> - **不要跳过 `https://`**（本就全是 URL，正是漏报高发区）。

- [ ] **Step 3c: 写测试 — JS 编译检查兜住裸写 `{{}}` 与其它语法错误**

```csharp
// tests/FlowEngine.Application.Tests/Workflows/WorkflowDraftValidatorTests.cs
[Fact]
public void CollectExpressionSyntaxErrors_BareMustacheUrl_Reported()
{
    // 裸写：无引号，JS 把 // 当注释 → 编译失败，JS 网能抓到
    var node = JsonNode.Parse("""{"id":"getEmployees","typeName":"httpRequest","parameters":{"url":"https://x?access_token={{$json.access_token}}"}}""")!;
    var errors = new List<string>();
    WorkflowDraftValidator.CollectExpressionSyntaxErrors(node["parameters"], "getEmployees", errors);
    Assert.NotEmpty(errors);
}

[Fact]
public void CollectExpressionSyntaxErrors_UnbalancedParens_Reported()
{
    var node = JsonNode.Parse("""{"id":"n","typeName":"script","parameters":{"code":"return ($json.a + "}}""")!;
    var errors = new List<string>();
    WorkflowDraftValidator.CollectExpressionSyntaxErrors(node["parameters"], "n", errors);
    Assert.NotEmpty(errors);
}
```

- [ ] **Step 4: 在 ValidateAsync 节点循环内调用扫描**

在 `WorkflowDraftValidator.ValidateAsync` 遍历节点的参数校验处，对每个节点的 `parameters` 同时调用两层扫描：
```csharp
CollectMustacheErrors(parametersNode, node.Id, errors);          // 首要防线：命中 {{ }} 即报错（带/不带引号）
CollectExpressionSyntaxErrors(parametersNode, node.Id, errors);  // 通用网：ScriptCompiler.TryCompile 兜住其它语法错误
```
（`errors` 最终映射到 `ValidateWorkflowResult.Errors`，由 `validate_workflow` 返回给 AI。）

- [ ] **Step 5: 运行测试确认通过**

Run: `dotnet test tests/FlowEngine.Application.Tests --filter "CollectMustacheErrors_|CollectExpressionSyntaxErrors_"`
Expected: PASS

- [ ] **Step 6: 提交**

```bash
git add backend/FlowEngine.Application/Workflows/WorkflowDraftValidator.cs tests/FlowEngine.Application.Tests/Workflows/WorkflowDraftValidatorTests.cs
git commit -m "feat(validation): scan mustache {{ }} templates in validate_workflow"
```

---

### Task 5: 运行时 Script 求值失败暴露到节点记录（次级安全网）

> **优先级说明：** Task 4 已在 `validate_workflow` 阶段（免执行）拦住 `{{ }}` 这类主要错误，因此本 Task 是**次级安全网**——仅用于兜住 validate 未能覆盖的、真正到运行时才暴露的脚本错误，并让报错带节点/参数信息，便于 AI 自纠。

**Files:**
- Modify: `backend/FlowEngine.Runtime/Executor/ScriptParameterPreEvaluator.cs` (PreEvaluateAsync 错误消息增强)
- Modify: `backend/FlowEngine.Runtime/Executor/NodeExecutionContextFactory.cs` (CreateAsync 异常捕获)
- Modify: `backend/FlowEngine.Runtime/Executor/WorkflowSchedulerKernel.cs` (执行节点 catch，构造失败记录)
- Test: `tests/FlowEngine.Runtime.Tests/Executor/ScriptParameterPreEvaluatorTests.cs`

**修正（编译错误修复）:** 原计划引用了多处不存在的符号，必须改正：
- `NodeExecutionRecord` **没有 `Status`/`Error` 属性**；失败信息在 `Output`（`NodeExecutionResult`，含 `Success`/`Error`）。
- 后端**不存在 `NodeExecutionStatus` 枚举**。
- `NodeError.NodeDefinitionId` 是 `string`，计划里 `NodeDefinitionId = node.Id`（Guid）需 `.ToString()`。
- `ScriptErrorException` 内部异常属性名是 `InnerException`，不是 `InnerError`。
- 测试里 `new ScriptContext(new NodeExecutionContext())` 应改为 `new ScriptContext(context)`；`new JsEngine()` 应改为 `JsEngine.Create(...)`。
- 原 Files 漏列 `WorkflowSchedulerKernel.cs`——本 Task 的 catch 实际落在该文件，必须补上。

- [ ] **Step 1: 写测试 — PreEvaluateAsync 失败时包含节点/参数信息**

```csharp
// tests/FlowEngine.Runtime.Tests/Executor/ScriptParameterPreEvaluatorTests.cs
[Fact]
public async Task PreEvaluateAsync_InvalidExpression_ThrowsWithNodeInfo()
{
    var rawParams = new Dictionary<string, object>
    {
        ["url"] = new Script { Source = "invalid {{{ syntax }}", ReturnType = ScriptReturnType.String }
    };
    var descriptor = new NodeTypeDescriptor { TypeName = "httpRequest" };
    var context = new ScriptContext(new NodeExecutionContext { Node = /* 构造带 Id/Name 的节点 */ });
    var js = JsEngine.Create();
    var cache = new ScriptCache(Options.Create(new JsEngineOptions()));

    var ex = await Assert.ThrowsAsync<ScriptErrorException>(() =>
        ScriptParameterPreEvaluator.PreEvaluateAsync(
            rawParams, descriptor, context, js, cache, default));

    Assert.Contains("url", ex.Message);
}
```

- [ ] **Step 2: 确认测试失败**

Run: `dotnet test tests/FlowEngine.Runtime.Tests --filter "PreEvaluateAsync_InvalidExpression_ThrowsWithNodeInfo"`
Expected: FAIL

- [ ] **Step 3: 增强异常信息（修正类型后）**

```csharp
// NodeExecutionContextFactory.cs CreateAsync 中捕获并增强
catch (ScriptErrorException ex)
{
    throw new ScriptErrorException(
        ex.Script,
        $"节点 \"{node.Id}\" 参数预求值失败: {ex.Message}",
        ex.InnerException);   // 注意：是 InnerException，不是 InnerError
}
```

```csharp
// WorkflowSchedulerKernel.cs 执行节点的 catch 块中（注意：record 在执行后才由 BuildNodeExecutionRecord 创建，
// 此处需新建失败记录，而非修改已有 record）
catch (ScriptErrorException ex)
{
    var failed = new NodeExecutionRecord
    {
        NodeId = node.Id,
        // NodeExecutionRecord 没有 Status/Error 属性；失败信息放在 Output
        Output = new NodeExecutionResult
        {
            Success = false,
            Error = new NodeError
            {
                Code = "ScriptError",
                Message = ex.Message,
                NodeDefinitionId = node.Id.ToString(),   // string，需 .ToString()
            },
        },
    };
    records.Add(failed);
}
```

- [ ] **Step 4: 运行测试确认通过**

Run: `dotnet test tests/FlowEngine.Runtime.Tests --filter "PreEvaluateAsync_"`
Expected: PASS

- [ ] **Step 5: 提交（含漏列的 WorkflowSchedulerKernel.cs）**

```bash
git add backend/FlowEngine.Runtime/Executor/ScriptParameterPreEvaluator.cs backend/FlowEngine.Runtime/Executor/NodeExecutionContextFactory.cs backend/FlowEngine.Runtime/Executor/WorkflowSchedulerKernel.cs tests/FlowEngine.Runtime.Tests/Executor/ScriptParameterPreEvaluatorTests.cs
git commit -m "fix(execution): expose Script evaluation errors in node records with node/param info"
```

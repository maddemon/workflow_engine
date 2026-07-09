# 任务：通过 CLI 创建「钉钉员工信息同步到数据库」流程并发现短板

## 目标

- 使用 Flow Engine CLI（`cli/`，命令 `flowengine`）端到端创建一条「从钉钉拉取员工信息并写入指定数据库」的工作流。
- 在创建过程中（CLI 命令、节点选型、凭据体系、DSL 编写）主动发现接口 / 插件 / CLI 层面的不足。
- 把发现的问题持续回填到本文档「短板发现」章节，作为后续能力建设的输入。

## 待完成项

- [ ] 设计目标流程的节点拓扑（trigger → 取 token → 拉用户 → 分页 → 字段映射 → 写库）
- [ ] 用 CLI 实际尝试建凭据、建工作流（`workflow create --dry-run` / 真建）
- [ ] 验证 CLI 离线/在线行为，记录连接、校验、报错方面的短板
- [ ] 汇总短板并回填下方「短板发现」章节，给出优先级与建议方向

## 完成标准

- 产出一份可评审的目标流程设计（JSON 草案）。
- 产出一份「短板清单」，每条带证据（文件:行号 或 CLI 实际输出）与建议归属（接口/插件/CLI/文档）。
- 不强行绕过安全约束（凭据不硬编码进工作流 JSON）。

## 目标流程设计（草案）

```text
credential "dingtalk"  (type=oauth2, 引擎托管令牌生命周期)
   tokenUrl=https://oapi.dingtalk.com/gettoken
   clientId=appKey, clientSecret=appSecret, grant=client_credentials
   （7200s 过期；引擎自动请求/缓存/刷新/重试，见 S2）

manualTrigger (isEntry)
   -> httpRequest "拉取部门用户列表"
        POST https://oapi.dingtalk.com/topapi/v2/user/list
             ?access_token={{ $credentials.dingtalk.accessToken }}   # 引用托管令牌
        auth: credential=dingtalk（Bearer 形态时直接用，query 形态时用表达式）
        body: { dept_id, cursor, size }
        返回 cursor 分页，需循环直到 next_cursor 为空
   -> [分页循环 paginate]  （当前引擎无对应节点，见 S4）
   -> script (JSNode) "字段映射"
        将钉钉 user 字段映射为数据库行结构
   -> [dbUpsert]  （用户确认需要，见 S1；待新增节点）
```

关键难点（对应短板章节编号）：

- 取 token 不应做成「钉钉专用节点」——太有针对性（企业微信/飞书又各需一个）。改为**通用 OAuth2 凭据能力**：凭据存储 `tokenUrl/clientId/clientSecret/grant`，引擎托管令牌请求/缓存/刷新/重试；HTTP 节点既可作 Bearer 鉴权，也可在表达式引用 `$credentials.<name>.accessToken`（覆盖钉钉 `?access_token=` 这类 query 形态）（S2）。
- 钉钉用户列表是 cursor 分页，引擎没有「重复调用 HTTP 直到 next_cursor 为空」的节点（S4）。
- 写库没有原生节点，用户确认需要 `dbUpsert`（S1，待新增）。
- 简单字段映射用 SetNode 不行（不支持表达式），只能用 JSNode（S5）。

## 短板发现

> 每条记录：编号 / 严重度 / 归属 / 现象 / 证据 / 建议方向。随探索持续更新。

### S1 — 缺少数据库写入节点（Critical，用户确认需要）
- 归属：插件
- 现象：标准插件（`plugins/FlowEngine.Plugins.Standard/`）仅有 `httpRequest` 等通用节点，没有任何 PostgreSQL / MySQL / SQL Server / 通用 SQL 连接器节点。无法把数据直接写入「指定数据库」。
- 用户反馈（2026-07-09）：`dbUpsert` 是明确需要的节点。
- 证据：`plugins/FlowEngine.Plugins.Standard/` 节点清单（无 DB 类节点）。
- 建议方向：新增通用 `DbUpsertNode`（不绑定具体 SaaS），支持 connectionString 凭据 + 参数化 SQL / 表名 + upsert 语义（按主键存在则更新否则插入）；具体方言（PG/MySQL/MSSQL）可用 connection 字符串或 `dialect` 参数区分。

### S2 — 缺少通用 OAuth2 令牌管理能力（High）
- 归属：接口 + 凭据体系 + 插件
- 现象：没有统一的「获取 access_token + 自动刷新/缓存」抽象。`HttpRequestNode` 的 `CredentialId` 只支持 Bearer/ApiKey/Basic 静态鉴权，无法代表「一个会过期的 OAuth2 令牌」。钉钉 gettoken 本质是 client_credentials 授权（`appKey`+`appSecret` → token，7200s 过期），企业微信、飞书等同理。
- 用户反馈（2026-07-09，设计取向）：
  - **平台专用节点（如 `DingTalkNode`）不应由引擎提供**——太有针对性，每加一个 SaaS 就要加一个节点；企业微信/飞书会无限膨胀。应提供**通用能力**。
  - 候选实现：① 强化 `HttpRequest`（内置 OAuth2 鉴权策略）；或 ② 新增通用 `OAuth2Node`。但用户指出 OAuth 的痛点：**「一旦拿到 access_token 通常就不需要再次请求」**，所以若做成节点，必须考虑：
    - **跳过再次请求**：命中未过期令牌则直接复用，不重发 token 请求；
    - **错误重试**：token 端点 5xx/超时做指数退避重试；
    - **过期刷新**：过期前 N 秒主动刷新（refresh token 续期）。
- 候选方案对比：
  - **方案 A（推荐）：凭据体系新增 `oauth2` 凭据类型，引擎托管令牌生命周期**。凭据存 `tokenUrl/clientId/clientSecret/scope/grant`；引擎自动发起 client_credentials 请求、**持久缓存 token（跨运行复用）、过期前 N 秒刷新、瞬时错误重试**；`HttpRequestNode` 可 (a) 直接作为 Bearer 鉴权，或 (b) 在表达式引用 `$credentials.<name>.accessToken`（覆盖钉钉 `?access_token=` 这种「token 进 query 而非 header」的形态）。→ **天然解决「拿到 token 就不需要再请求」**：token 由凭据层持久缓存，运行时永不重复请求，无需在节点里做 skip 逻辑。（缓存*介质*为实现细节，见 `plan-004` §5 风险；与本文「持久缓存」意图一致，仅落盘方式待定。）
  - **方案 B：通用 `OAuth2Node` 节点**。支持 client_credentials/authorization_code，输出 token 到变量。需额外实现用户提到的两点：① 按 `clientId+scope` 缓存、命中未过期则跳过请求；② 5xx/超时指数退避重试 + refresh 续期。仅在「需要将 token 当作数据在流程中流转」时作为方案 A 的补充。
  - **方案 C：强化 `HttpRequestNode` auth 字段**（类 n8n 内置 OAuth2）。与方案 A 在 Bearer 形态重叠，但无法覆盖 query 形态，通用性弱于 A。
- 结论（2026-07-09 用户最终裁定）：
  - **OAuth2 能力（generic）是必须的**：本地部署（私有化）的钉钉/企业微信，官方 SDK 云端点假设不成立，只有通用 OAuth2（`tokenUrl` 可配置）+ `httpRequest` 能兜底 → 见 `plan-004` 主路径。
  - **平台专用节点（如 `DingTalkNode`）默认先不做**：其形态是「引用官方 SDK 程序集 + 薄封装，暴露多个方法，一个节点匹配一个平台」，作为**未来扩展模板**，不在当前计划实现，后续再考虑。
- 证据：`plugins/FlowEngine.Plugins.Standard/HttpRequestNode.cs:91-98`（`Authentication` 枚举仅 None/BearerToken/ApiKey/BasicAuth）；`CredentialId` 仅用于 `ResolveApiKeyAsync`；凭据 `Type` 为任意字符串无枚举（见 S8）。

### S3 — 凭据多字段无法在表达式中引用（High，安全相关）
- 归属：接口 + 凭据体系
- 现象：凭据只能以 `apiKey` 单字段被 `HttpRequestNode` 消费；节点参数/表达式无法读取 `credential.fields.appKey` 等任意字段。两步走令牌流（需要 appKey+appSecret 作请求参数）被迫把 secret 硬编码进工作流 JSON，违反 `project-rules.md` 安全约束。
- 证据：`FlowConstants.CredentialFields.ApiKey` 仅 `apiKey`（`backend/FlowEngine.Core/FlowConstants.cs:31-34`）；`HttpRequestNode.cs:96` 的 `[Credential(FlowConstants.CredentialFields.ApiKey)]`。
- 建议方向：在表达式上下文暴露 `credentials.<name>.<field>` 或 `credential('<name>').fields.appKey`；支持多字段凭据在 body/header 表达式中插值。

### S4 — 无游标分页 / 循环调用 API 的能力（High）
- 归属：插件
- 现象：`LoopNode` 仅对已收集的 `DataBatch` 做分批（`plugins/.../LoopNode.cs:54-141`），不会重新调用上游 API 拉取下一页。钉钉用户列表为 cursor 分页，引擎缺少「重复调用 HTTP 直到 next_cursor 为空」的节点/机制。
- 证据：`LoopNode.cs` 的 `HandleNextBatch` 仅对 `inputBatch.Items` 做 `Skip/Take`，无外部调用。
- 建议方向：新增 `PaginateNode`（基于 nextCursor/nextPage 反复触发上游 HTTP 节点），或让 `LoopNode` 支持「回环到 HTTP 节点并重试直至终止条件」。
- 设计补遗（2026-07-09，见 `plan-004` §3.4）：`PaginateNode` 采用「内置 HTTP 循环」，**每次迭代向请求表达式作用域注入 `$cursor`**（初值 `cursorInitial`），`url`/`bodyExpression` 通过 `={{ $cursor }}` 引用，响应后按 `nextCursorPath` 覆盖——**游标推进在节点内部完成，不依赖全局 `$vars` 状态变量**；通用 `LoopNode` 不重调上游的缺口仍保留（非分页的「重调 API」场景未来另议）。

### S5 — SetNode 不支持表达式（Medium）
- 归属：插件
- 现象：`SetNode.ParseValue` 只做 JSON/数字/布尔/字符串解析（`plugins/.../SetNode.cs:111-141`），不解析 `={{ }}`；简单字段重命名/映射只能改用 `script`(JSNode)，偏重。
- 证据：`SetNode.cs:82-94` 直接 `ParseValue(field.Value)`。
- 建议方向：SetNode 字段值支持 `={{ }}` 表达式，与 `HttpRequestNode` 的 `ScriptEngine.Evaluate*` 对齐。

### S6 — CLI 强依赖运行中的后端，无离线校验（Medium）
- 归属：CLI
- 现象：`guide` / `node-types list` / `workflow create` 都需连接 `http://localhost:8001` 并已登录；`workflow create --dry-run` 仅打印请求体，**不校验节点类型/端口/连接图**。建流程前无法本地发现图是否合法。
- 证据（CLI 实测，2026-07-09）：
  - `node-types list` → 输出 `网络请求失败`，`EXIT=2`（CLIError 网络错误）；无后端时整套能力查询不可用。
  - `workflow create --dry-run --file dingtalk-sync-draft.json` 接受了**不存在的节点类型** `dingtalkGetToken` / `paginate` / `dbUpsert`，仅打印请求体，`EXIT=0`，全程**零校验**（不查节点类型、不查端口、不查连接图）。
  - 相关代码：`cli/src/config.ts:6`（`DEFAULT_BASE_URL='http://localhost:8001'`）；`cli/src/commands/workflows.ts:345-353`（`--dry-run` 只 `JSON.stringify(dto)`）。
- 建议方向：CLI 提供 `workflow validate <file>`（本地基于已缓存 node-types schema 校验节点类型/端口/连接图），`--dry-run` 增加基础结构校验；允许指定离线 schema 包。

### S7 — CLI 无法暴露「能力缺口」（Medium）
- 归属：CLI + 文档
- 现象：`guide` 在无后端时回退到硬编码的两个示例（`HelloHttp` / `ConditionalFlow`），输出 `## 支持的节点类型` 为 `（无）`，不会提示「没有数据库节点 / 钉钉节点」。用户在尝试前无从得知能力缺口。
- 证据（CLI 实测，2026-07-09）：`guide` 后端不可用输出片段：
  ```text
  获取节点类型失败：网络请求失败
  # Flow Engine DSL 编写指南
  > 注意：当前无法获取后端节点类型清单，以下内容为基础模板。
  ## 支持的节点类型
  （无）
  ## 示例工作流
  ### 基础 HTTP 请求工作流   # 仅 HelloHttp
  ### 条件分支工作流         # 仅 ConditionalFlow
  ```
- 代码：`cli/src/commands/guide.ts:121-258`（`buildExamples()` 仅两个写死示例）；`guide.ts:339-350`（获取节点类型失败仅 `incomplete=true`，不列举已知缺口）。
- 建议方向：`guide` 在 `incomplete` 时明确提示「未连接后端，节点清单不可用」；并维护一份内置节点能力说明（含「已知缺失：DB/DingTalk 连接器」）。

### S8 — 凭据 Type 无枚举 / 字段 schema 校验（Low）
- 归属：接口 + CLI
- 现象：`CreateCredentialDto.Type` 是任意字符串（`cli/src/types.ts:350-355`、`backend/.../CredentialService.cs` 不校验 type）；CLI `credential create --type` 也无校验；无预置 dingtalk/http 等类型与字段 schema，易拼错、无文档化字段。
- 建议方向：定义凭据类型注册表（含字段 schema），CLI 提供 `credential types` 列出已知类型与必填字段。

### S9 — 表达式变量模型未文档化且不一致（Medium）
- 归属：接口 + 文档
- 现象：不同节点对「表达式」的支持与变量名不一致，且全无文档：
  - `httpRequest` 的 URL/Header/Body 用裸 JS（`'https://api.com/' + input.path`）或 `={{ }}` 包裹；
  - `if` 节点示例用 `={{ $json.value > 10 }}`（引入 `$json`）；
  - `SetNode`/`LoopNode` 根本不解析表达式；
  - 草稿里写的 `={{ $credentials.dingtalk.appKey }}` **没有任何节点/引擎提供 `$credentials` 变量**（与 S3 呼应，运行时必然失败）。
- 证据：`cli/src/commands/guide.ts:191`（`if` 示例 `={{ $json.value > 10 }}`）；`HttpRequestNode.cs:80-85,107-133`（URL/Header/Body 走 `ScriptEngine.Evaluate*`，裸 JS）；`SetNode.cs:82-94`（不解析表达式）；`JSNode.cs:106-135`（仅注入 `$input`）。
- 建议方向：统一表达式入口与变量模型（如 `$json`/`$input`/`$credentials`/`$vars`），在文档与 `guide` 中给出变量清单与语法规范；`SetNode` 等补齐表达式支持（呼应 S5）。
- 关联：本流程 draft 已使用 `={{ $credentials.dingtalk.accessToken }}` / `={{ $credentials.db.connectionString }}`，但当前引擎**尚不支持** `$credentials`（依赖 S3 / `plan-004` 阶段一），属计划前置项。

## 主要修改记录

- 2026-07-09：基于代码探索（CLI、HttpRequestNode、SetNode、LoopNode、CredentialService、FlowConstants）完成目标流程设计与 S1–S8 短板初稿。
- 2026-07-09：CLI 实测验证 S6/S7（`node-types list` 网络失败；`workflow create --dry-run` 接受不存在节点类型且零校验；`guide` 回退写死示例且不暴露缺口），回填证据；新增 S9（表达式变量模型未文档化/不一致）。
- 2026-07-09：据用户设计反馈更新——S1 标注 `dbUpsert` 为**用户确认需要**；S2 由「钉钉专用节点」改为**通用 OAuth2 凭据能力**（方案 A 凭据层托管令牌生命周期，天然解决「拿到 token 就不需要再请求」+ 错误重试 + 过期刷新），明确「平台专用节点不该由引擎提供」；同步草稿 `workflows/dingtalk-sync-draft.json` 移除 `dingtalkGetToken` 节点。

# Task: plan-004 文档同步

## 目标

更新架构文档，使其与 plan-004 已落地的集成基础能力保持一致。

## 待完成项

1. `docs/architecture/credentials.md`
   - 补充凭据类型注册表（`CredentialTypeRegistry` / `ICredentialTypeRegistry`）与字段 schema。
   - 补充内置类型（`apiKey` / `connectionString` / `basicAuth` / `oauth2`）及字段定义。
   - 补充 OAuth2 令牌生命周期管理（获取 / 缓存 / 刷新 / 重试）。
   - 补充 `$credentials.<name>.<field>` 表达式注入与 `OAuth2CredentialAccessor`。

2. `docs/architecture/expression-system.md`
   - 更新变量模型为 `$` 前缀内建变量（`$json` / `$input` / `$items` / `$node` / `$workflow` / `$execution` / `$env` / `$vars` / `$now` / `$today` / `$runIndex` / `$itemIndex` / `$credentials` / `$ctx`）。
   - 说明函数式与省略式双写法、基于 Acornima AST 的表达式分类。
   - 说明节点私有变量通过 `extraGlobals` 本地注入（`$cursor` / `$nextCursor` / `$page` / `$response` 等）。

3. `docs/architecture/node-system.md`
   - 补充 `DbUpsertNode`、`OAuth2Node`、`PaginateNode` 的参数、执行语义与输出。
   - 更新节点分类示例与参数定义字段说明。

## 验收标准

- [x] `credentials.md` 已补充凭据类型注册表、OAuth2 令牌生命周期、`$credentials.<name>.<field>` 表达式访问。
- [x] `expression-system.md` 已更新 `$` 前缀内建变量模型、表达式分类与 `extraGlobals` 节点私有变量说明。
- [x] `node-system.md` 已补充 `DbUpsertNode`、`OAuth2Node`、`PaginateNode` 的参数与执行语义。
- [x] `dotnet build`、`dotnet test`、`npm test` 全部通过。

## 完成状态

已完成。验证结果：

- `dotnet build FlowEngine.sln` ✅
- `dotnet test FlowEngine.sln` ✅
- `npm test` (cli) ✅ 143 tests passed

# 凭据管理

> 本文档基于当前代码编写，以代码为准。加密实现以 `FlowEngine.Infrastructure/Security/CredentialEncryptionService.cs`、服务层 `FlowEngine.Application/Credentials/CredentialService.cs`、运行时注入 `FlowEngine.Runtime/Credentials/CredentialAccessor.cs` 为权威来源。

凭据（Credential）用于保存第三方系统的敏感信息，如 API Token、数据库密码、OAuth 密钥等。Flow Engine 的核心安全原则是：**凭据静态加密存储，运行时解密注入；明文不落日志、不返回前端；节点只按 ID 引用凭据，绝不内嵌密钥。**

## 1. 加密存储（AES-256-GCM）

凭据在落库前逐字段加密，实现见 `CredentialEncryptionService`：

- 算法：**AES-256-GCM**（认证加密，同时保证机密性与完整性）。
- 每字段使用独立随机 **12 字节 Nonce**，输出 **16 字节 Tag**。
- 密文、Nonce、Tag 以十六进制字符串存入 `EncryptedField`（含 `IsBinary` 标记区分文本 / 二进制）。
- 加密密钥由 `ICryptoKeyProvider` 提供，凭据实体记录 `KeyVersion`（当前 `v1`），支持未来密钥轮换。

> 对应 [系统总览](architecture/overview.md) 第 9 节：凭据静态加密（AES-GCM），运行时解密注入；明文不落日志、不返回前端。

## 2. 创建与维护

凭据的 CRUD 由 `CredentialService` 编排：

- `CreateAsync` / `EnsureAsync`（幂等：按 `(Name, Type, ProjectId)` 存在则覆盖字段）创建凭据。
- `UpdateAsync` 更新名称与字段；更新时重新加密。
- `DeleteAsync` 删除凭据；**若凭据仍被工作流引用，则返回 `ReferencedBy` 引用列表而拒绝删除**（避免悬空引用）。
- 创建 / 更新时对字段做 `CredentialTypeRegistry.Validate` 类型校验，并对 `Name` 做项目内唯一性校验。

**在 UI 中创建凭据**：登录后在 Flow Engine 前端的凭据管理入口填写名称、类型与字段值即可（具体菜单路径请在 UI 中确认）。也可经 API / MCP 技能间接创建。MCP 鉴权所用的 API Key 不在凭据体系内，见 [AI Agent IDE（MCP）](ai-agent-mcp.md) 第 2 节，由 **帮助与 MCP 配置** 页面提供。

## 3. 凭据脱敏与前端返回

读取凭据时 `CredentialService.MapToDto` 依据调用者角色决定是否脱敏：

- 需脱敏场景：字段值直接返回占位符 `"***"`，**不执行解密**（避免明文外泄到前端 / 日志）。
- 授权场景（如凭据所有者或具备相应角色）：返回解密后的字段值。

即：前端拿到的凭据详情要么是无意义的 `***`，要么是经授权的解密值；**原始密钥不会出现在日志或异常信息中**。

## 4. 运行时注入（按 ID 引用）

工作流执行时，节点参数**只保存凭据 ID**，引擎在解析参数阶段按 ID 取回明文：

- `CredentialAccessor.GetCredentialAsync(Guid credentialId)`：按 ID 加载凭据（只读、不跟踪），用对应 `KeyVersion` 的密钥逐字段解密，返回 `CredentialValue`（含 `Fields` 文本字段与 `BinaryFields` 二进制字段）。
- 执行引擎在「解析参数 → 解密凭据 → 执行 → 输出下游」流程中完成注入（见 [系统总览](architecture/overview.md) 第 6 节）。

节点插件**只声明凭据 ID 参数，绝不接收明文密钥**；明文仅在节点执行内存中短暂存在，用于实际外部调用。

## 5. 在节点中引用凭据

以极简示例（MCP `assemble_workflow` 草稿）说明：某 HTTP 节点需携带 Bearer Token，参数只填凭据 ID：

```json
{
  "id": "fetch",
  "typeName": "httpRequest",
  "parameters": {
    "url": "https://api.example.com/data",
    "credentialId": "3f1a...（凭据 GUID）"
  }
}
```

> 具体字段名（如 `credentialId` 还是 `credential`）随节点类型而定，以 `get_node_detail` 返回的 schema 为准（见 [AI Agent IDE（MCP）](ai-agent-mcp.md)）。原则是：**值永远是凭据 ID 字符串，不是密钥本身**。

若引用的凭据不存在，节点执行 / 校验会报 `CredentialNotFound`，需先在 UI 或 API 创建该凭据。

## 6. 安全要点小结

| 关注点 | 行为 |
|--------|------|
| 静态存储 | AES-256-GCM 逐字段加密，含 Nonce + Tag，密钥带版本号 |
| 传输给前端 | 脱敏为 `***` 或经授权解密；原始密钥不返回 |
| 日志 / 异常 | 明文密钥不写入日志、不进入异常信息 |
| 节点引用 | 仅按凭据 ID 引用，运行时解密注入 |
| 删除保护 | 被工作流引用时拒绝删除，返回引用列表 |

## 7. 待确认 / 备注

- 前端「凭据管理」页面的确切菜单名称与填写步骤以 UI 实际为准（本文未逐一核对 UI 文案）。
- 密钥来源（`ICryptoKeyProvider` 如何从配置 / KMS 取密钥）属于部署配置范畴，本文不涉及具体密钥托管方案。

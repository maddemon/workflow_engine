# 任务：后端 Infrastructure 模块测试补充

## 目标

将 `FlowEngine.Infrastructure` 行覆盖率从 **41.7%**（Task 008 实测，覆盖行 365 / 875）提升至 **65%+**。距目标 ~23pt，为后端冲 75% 的最大单一缺口，高投入；优先补存储、身份哈希、Token 服务、审计/调度等适配器逻辑。

**行号说明**：文中 `:行号`（如 `:41`）取自 2026-07-17 版本源码，仅作辅助参考；执行时请以类名 / 方法名 / 签名为准确认当前源码，行号可能因后续改动偏移。

## 目标类与已核实 API

### LocalFileStorage
- 命名空间 `FlowEngine.Infrastructure.Storage`，`Storage/LocalFileStorage.cs:9`，`public sealed class LocalFileStorage : IFileStorage`
- 主构造：**`public LocalFileStorage(string basePath = "./storage/files", ILogger<LocalFileStorage>? logger = null)`** :17（**无 `FileStorageOptions` / `BasePath` 类**，base path 为普通 string）。
- 公共方法：
  - `public async Task<string> SaveAsync(string fileName, Stream content, string projectId, CancellationToken ct = default)` :24
  - `public Task<Stream?> ReadAsync(string fileId, CancellationToken ct = default)` :50
  - `public Task<bool> DeleteAsync(string fileId, CancellationToken ct = default)` :72
  - `public Task<bool> ExistsAsync(...)` :89
- 注意：**无 `GetByIdAsync`**；`SaveAsync` 第 3 参为 `projectId`（须为合法 GUID），**无 contentType 参数**（原草稿 `SaveAsync("test.txt", stream, "text/plain")` 错误）。

### PasswordHasher
- 命名空间 `FlowEngine.Infrastructure.Identity`，`Identity/PasswordHasher.cs:10`，`: IPasswordHasher`（接口在 `FlowEngine.Application.Identity`）
- 真实签名：
  - `public string HashPassword(string password)` :16
  - `public PasswordVerifyResult VerifyPassword(string hashedPassword, string password)` :22（返回枚举 `PasswordVerifyResult`：`Failed` / `Success` / `SuccessRehashNeeded`）
- 注意：方法名为 `HashPassword` / `VerifyPassword`，参数顺序 `(hashedPassword, password)`（原草稿 `Hash` / `Verify` 错误）。

### JwtTokenService
- 命名空间 `FlowEngine.Infrastructure.Identity`，`Identity/JwtTokenService.cs:13`，`: ITokenService`
- 主构造：**`public class JwtTokenService(IConfiguration configuration)`**（**非 `IOptions<JwtOptions>`**，原草稿错误）。
- 公共方法：`public string GenerateAccessToken(Guid userId, string email, IReadOnlyList<string> roles)` :16
- 注意：**无 `ValidateToken`**；**无 `JwtOptions` 类**（grep `class JwtOptions` 无命中）；密钥/签发方/受众/过期从配置 `Jwt:Secret` / `Jwt:Issuer` / `Jwt:Audience` / `Jwt:ExpirationMinutes` 读取。

## 待完成项

- [ ] **4.1 LocalFileStorage 测试**：用临时目录作 `basePath`，覆盖 `SaveAsync`（含非法 `projectId` 校验，参考 :96-103）→ `ReadAsync` 往返 → `ExistsAsync` → `DeleteAsync`；验证返回 fileId 可回读。
- [ ] **4.2 PasswordHasher 测试**：`HashPassword` 后 `VerifyPassword` 返回 `Success`；错误密码返回 `Failed`；同密码两次哈希不同（加盐）；`SuccessRehashNeeded` 场景（如需要）。
- [ ] **4.3 JwtTokenService 测试**：构造 `ConfigurationBuilder` / `IConfiguration` 注入 `Jwt:*` 配置，`GenerateAccessToken` 产出非空 token；验证 roles/email 进入声明（可解析 JWT payload 或依赖现有校验）。

## 完成标准

- `dotnet test tests/FlowEngine.Infrastructure.Tests` 全绿（该项目已存在）。
- 不使用 `FluentAssertions` / `Moq`（用 `Assert.*` + 真实临时文件系统 / `ConfigurationBuilder`）。
- 所有签名与上文核实一致。

- 对应项目 `dotnet build` 通过（无编译错误，新增测试不得引入类型/签名错误）。

## 完成状态

- [x] 4.1 LocalFileStorage 测试已补充（临时目录、Save/Read/Exists/Delete、非法 projectId、路径穿越等）。
- [x] 4.2 PasswordHasher 测试已补充（哈希/验证、错误密码、两次哈希不同、V2 RehashNeeded）。
- [x] 4.3 JwtTokenService 测试已补充（配置构造、claims 校验、缺密钥异常、默认过期等）。

**实测覆盖率**：`FlowEngine.Infrastructure` 行覆盖率 `41.7%` → `66.97%`（≥65%，达标）。

## 主要修改记录

- 重写自 `plan-unit-test-coverage.md`：修正 `LocalFileStorage` 构造与 `SaveAsync` 参数顺序、`PasswordHasher.Hash/Verify`→`HashPassword/VerifyPassword`、JwtTokenService `IOptions<JwtOptions>`→`IConfiguration` 及 `ValidateToken`/`JwtOptions` 虚构项。

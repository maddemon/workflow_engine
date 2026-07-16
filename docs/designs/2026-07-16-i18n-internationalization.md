# 国际化（i18n）设计

## 1. 概述

为 Flow Engine 添加英文/中文双语支持，采用可扩展架构，方便社区贡献者添加更多语言。

| 层 | 方案 | 格式 |
|---|---|---|
| 前端 | react-i18next | `public/locales/{lang}/translation.json` |
| 后端 | ASP.NET Core `IStringLocalizer<T>` | `.resx` 资源文件 |

**语言代码约定**：使用 BCP-47 标准（`en`、`zh-CN`），前后端统一。不混用 `zh`、`zh_CN`、`zh-Hans`。

## 2. 前端架构

### 2.1 初始化

新建 `frontend/src/i18n.ts`：

```typescript
import i18n from 'i18next';
import { initReactI18next } from 'react-i18next';
import LanguageDetector from 'i18next-browser-languagedetector';
import Backend from 'i18next-http-backend';

i18n
  .use(Backend)                     // 从 /locales/{lang}/translation.json 加载
  .use(LanguageDetector)            // 自动检测浏览器语言
  .use(initReactI18next)            // 自动注入 React Context，无需手动包 Provider
  .init({
    fallbackLng: 'en',
    supportedLngs: ['en', 'zh-CN'],
    nonExplicitSupportedLngs: true,  // 浏览器 zh 也能匹配到 zh-CN
    ns: ['common', 'login', 'settings', 'workflow', 'nodePanel', 'parameterPanel', 'execution', 'admin', 'header'],
    defaultNS: 'common',
    interpolation: { escapeValue: false },  // React 已做 XSS 防护
    backend: {
      loadPath: '/locales/{{lng}}/{{ns}}.json',  // 显式指定路径，避免子路径部署失败
    },
    detection: {
      order: ['localStorage', 'navigator'],
      caches: ['localStorage'],
      lookupLocalStorage: 'i18nextLng',
    },
  });
```

- 语言偏好存入 `localStorage`，刷新后保留
- 首次访问自动检测浏览器语言，匹配到 `supportedLngs` 则自动切换
- 按 namespace 拆分翻译文件，页面只加载所需的 namespace（懒加载）

### 2.2 入口注入

`main.tsx` 中导入 `i18n.ts` 确保在应用渲染前初始化。`initReactI18next` 已自动注入 React Context，无需额外包 Provider：

```typescript
import './i18n';  // i18n init — 必须是第一个 import，确保 useTranslation 可用
// 不再需要 import { I18nextProvider } — initReactI18next 已自动注入

root.render(
  <MantineProvider>
    <App />
  </MantineProvider>,
);
```

### 2.3 在组件中使用

```tsx
import { useTranslation } from 'react-i18next';

export function LoginPage() {
  const { t } = useTranslation();

  return (
    <Title order={3}>{t('login.title')}</Title>
    <TextInput label={t('login.email')} />
    <Button>{t('login.signIn')}</Button>
  );
}
```

含内嵌 HTML 时用 `<Trans>` 组件，须显式声明 `components`：

```tsx
import { Trans } from 'react-i18next';

<Trans i18nKey="settings.keyCreated" components={{ strong: <strong /> }}>
  Copy this key now. It will <strong>not</strong> be shown again.
</Trans>
```

### 2.4 语言切换组件

```tsx
import { useTranslation } from 'react-i18next';

export function LanguageSwitcher() {
  const { i18n } = useTranslation();
  return (
    <Select
      value={i18n.language}
      onChange={(val) => i18n.changeLanguage(val!)}
      data={[
        { value: 'en', label: 'English' },
        { value: 'zh-CN', label: '中文' },
      ]}
    />
  );
}
```

放置在 `HeaderToolbar` 中。

> **Mantine locale 同步**：切换语言时需同步更新 `@mantine/dates` 等组件库的 locale 设置，可通过 `useEffect` 监听 `i18n.language` 变化后调用 `setLocale` 实现。

### 2.5 API 请求自动携带语言

在 `services/api.ts` 的 axios 实例中添加拦截器，自动将当前语言通过 `Accept-Language` header 发送给后端。使用 `resolvedLanguage` 防御初始化完成前返回 `undefined`：

```typescript
import i18n from '../i18n';

api.interceptors.request.use((config) => {
  config.headers.set('Accept-Language', i18n.resolvedLanguage ?? 'en');
  return config;
});
```

### 2.6 翻译文件结构

**Key 命名规范**：`{module}.{component}.{purpose}`，全小写，点号分隔。

```
common.save                 — 通用操作
login.email                 — 登录页
settings.apiKeys.createKey  — 设置页 API Key 管理
execution.panel.title       — 执行面板
```

按 namespace 拆分文件，每个文件对应一个模块：

`public/locales/en/common.json`：

```json
{
  "save": "Save",
  "cancel": "Cancel",
  "delete": "Delete",
  "create": "Create",
  "edit": "Edit",
  "loading": "Loading…",
  "error": "Error",
  "noData": "No data",
  "confirmDelete": "Are you sure you want to delete this?",
  "confirmDiscard": "You have unsaved changes. Discard them?"
}
```

`public/locales/en/login.json`：

```json
{
  "title": "Sign In",
  "subtitle": "Enter your credentials to continue",
  "email": "Email",
  "password": "Password",
  "signIn": "Sign In",
  "failed": "Login failed",
  "unexpectedError": "An unexpected error occurred"
}
```

`public/locales/en/settings.json`：

```json
{
  "title": "Settings",
  "userInfo": "User Info",
  "email": "Email",
  "userName": "User Name",
  "displayName": "Display Name",
  "createdAt": "Created At",
  "roles": "Roles",
  "noRoles": "No roles assigned",
  "apiKeys": {
    "title": "API Keys",
    "create": "Create API Key",
    "keyName": "Key Name",
    "nameRequired": "Key name is required",
    "created": "Created",
    "expires": "Expires",
    "status": "Status",
    "actions": "Actions",
    "active": "Active",
    "revoked": "Revoked",
    "expired": "Expired",
    "revoke": "Revoke",
    "revokeConfirm": "Are you sure you want to revoke the API key <strong>{{name}}</strong>?",
    "revokeDesc": "This action cannot be undone. Any services using this key will lose access immediately.",
    "keyCreated": "Copy this key now. It will <strong>not</strong> be shown again.",
    "copied": "Copied",
    "copyToClipboard": "Copy to clipboard",
    "loading": "Loading API keys…",
    "noKeys": "No API keys yet. Create one to get started.",
    "createFailed": "Failed to create API key",
    "revokeFailed": "Failed to revoke API key"
  }
}
```

`public/locales/en/workflow.json`：

```json
{
  "title": "Workflows",
  "new": "New Workflow",
  "search": "Search workflows…",
  "noWorkflows": "No workflows yet",
  "import": "Import Workflow",
  "export": "Export Workflow",
  "confirmDelete": "Delete workflow \"{{name}}\"? This cannot be undone."
}
```

`public/locales/en/nodePanel.json`：

```json
{
  "title": "Nodes",
  "search": "Search nodes…",
  "noResults": "No nodes found"
}
```

`public/locales/en/parameterPanel.json`：

```json
{
  "title": "Properties",
  "noSelection": "Select a node to configure"
}
```

`public/locales/en/execution.json`：

```json
{
  "title": "Execution",
  "run": "Run",
  "stop": "Stop",
  "status": {
    "idle": "Idle",
    "running": "Running…",
    "completed": "Completed",
    "failed": "Failed",
    "cancelled": "Cancelled"
  },
  "noExecutions": "No executions yet",
  "output": "Output",
  "error": "Error",
  "duration": "Duration",
  "startedAt": "Started at",
  "completedAt": "Completed at"
}
```

`public/locales/en/admin.json`：

```json
{
  "users": "Users",
  "projects": "Workspaces",
  "files": "Files",
  "audit": "Audit Log",
  "addUser": "Add User",
  "inviteUser": "Invite User",
  "removeUser": "Remove User",
  "confirmRemove": "Are you sure you want to remove this user?"
}
```

`public/locales/en/header.json`：

```json
{
  "workflows": "Workflows",
  "executions": "Executions",
  "settings": "Settings",
  "admin": "Admin",
  "help": "Help",
  "signOut": "Sign Out"
}
```

`public/locales/zh-CN/` 下对应翻译文件，key 结构完全一致，value 为中文字符串。

## 3. 后端架构

### 3.1 新建资源项目

新建 `backend/FlowEngine.Resources/` 项目，包含 `SharedResource.cs` 标记类和 `.resx` 资源文件。

`SharedResource.cs`：

```csharp
namespace FlowEngine.Resources;

/// <summary>
/// 共享本地化资源的标记类。
/// 配合 IStringLocalizer<SharedResource> 注入使用。
/// </summary>
public class SharedResource;
```

`FlowEngine.Resources.csproj` 关键配置：

```xml
<ItemGroup>
  <!-- 将 resx 文件的 LogicalName 映射为完整类型名，确保 IStringLocalizer 能定位 -->
  <EmbeddedResource Update="SharedResource.resx"
                    LogicalName="FlowEngine.Resources.SharedResource.resources" />
  <EmbeddedResource Update="SharedResource.zh-CN.resx"
                    LogicalName="FlowEngine.Resources.SharedResource.zh-CN.resources" />
</ItemGroup>
```

> 资源文件和 `SharedResource.cs` 放在同一目录。默认情况 `.resx` 的嵌入资源名会包含目录结构（如 `FlowEngine.Resources.Resources.SharedResource.resources`），导致 `IStringLocalizer<SharedResource>` 找不到，因此必须通过 `LogicalName` 显式覆盖。

`FlowEngine.Host.csproj` 需添加项目引用：

```xml
<ItemGroup>
  <ProjectReference Include="..\FlowEngine.Resources\FlowEngine.Resources.csproj" />
</ItemGroup>
```

### 3.2 resx 资源文件

默认语言文件不带区域后缀，fallback 文化直接匹配：

`SharedResource.resx`（默认/英文）：

```xml
<?xml version="1.0" encoding="utf-8"?>
<root>
  <data name="WorkflowNotFound" xml:space="preserve">
    <value>Workflow not found</value>
  </data>
  <data name="NodeNotFound" xml:space="preserve">
    <value>Node not found</value>
  </data>
  <data name="ExecutionNotFound" xml:space="preserve">
    <value>Execution not found</value>
  </data>
  <data name="Unauthorized" xml:space="preserve">
    <value>Unauthorized</value>
  </data>
  <data name="Forbidden" xml:space="preserve">
    <value>Forbidden</value>
  </data>
  <data name="ValidationFailed" xml:space="preserve">
    <value>Validation failed</value>
  </data>
  <data name="InternalServerError" xml:space="preserve">
    <value>An internal error occurred</value>
  </data>
  <data name="WorkflowNameRequired" xml:space="preserve">
    <value>Workflow name is required</value>
  </data>
  <data name="InvalidExpression" xml:space="preserve">
    <value>Invalid expression: {0}</value>
  </data>
  <data name="CredentialNotFound" xml:space="preserve">
    <value>Credential not found</value>
  </data>
  <data name="ApiKeyNameRequired" xml:space="preserve">
    <value>API key name is required</value>
  </data>
  <data name="ApiKeyRevoked" xml:space="preserve">
    <value>API key "{0}" has been revoked</value>
  </data>
  <data name="ApiKeyCreateFailed" xml:space="preserve">
    <value>Failed to create API key</value>
  </data>
  <data name="ApiKeyRevokeFailed" xml:space="preserve">
    <value>Failed to revoke API key</value>
  </data>
</root>
```

`SharedResource.zh-CN.resx` 为对应中文翻译。

> .resx 是纯 XML 格式，任意文本编辑器均可修改，不依赖 Visual Studio。参数占位符使用 `{0}`、`{1}` 格式（.NET 标准），与前端 i18next 的 `{{key}}` 不同。`IStringLocalizer` 的索引器接受格式化参数：`localizer["ApiKeyRevoked", keyName]`。
>
> **限制**：.resx 不原生支持复数形式（如 "1 workflow" / "2 workflows"）。当前错误消息均为单数，无此需求。未来如需复数，需自定义 `IPluralStringLocalizer` 或改用其他方案。

### 3.3 配置 RequestLocalization

```csharp
using System.Globalization;

// 注册本地化服务
builder.Services.AddLocalization();

// DataAnnotations 验证消息本地化（如 [Required] 等）
builder.Services.AddControllers()
    .AddDataAnnotationsLocalization();

// 支持的语言
var supportedCultures = new[] {
    new CultureInfo("en"),
    new CultureInfo("zh-CN"),
};

app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new("en"),
    SupportedCultures = supportedCultures,
    SupportedUICultures = supportedCultures,
});
```

> `.resx` 是 ASP.NET Core 内置支持，零额外依赖。JSON 格式可在实施阶段评估社区方案（如 `PoLocalization`），但 `.resx` 已足够满足需求。

### 3.4 在 Controller / Service 中使用

**关键原则**：`errorCode` 保持英文机器码不变（前端依赖其做分支判断），**只本地化 `message`**。

```csharp
public class WorkflowsController(
    IWorkflowService workflowService,
    IStringLocalizer<SharedResource> localizer) : ControllerBase
{
    [HttpGet("{id}")]
    public async Task<ActionResult<WorkflowDto>> Get(Guid id)
    {
        var workflow = await workflowService.GetAsync(id);
        if (workflow is null)
            return NotFound(new { success = false, errorCode = "WorkflowNotFound", message = localizer["WorkflowNotFound"] });
        return Ok(workflow);
    }

    // 带参数的消息：localizer["ApiKeyRevoked", keyName]
    // .resx 中定义 "API key \"{0}\" has been revoked"
    // localizer 自动做 string.Format 替换
}
```

### 3.5 统一错误响应本地化

当前仓库没有统一错误响应中间件，Controller 在各 catch 处内联返回错误。实施步骤：

1. 新建统一异常中间件 `GlobalExceptionHandlerMiddleware`，集中处理 `DomainException` 等已知异常
2. 中间件中注入 `IStringLocalizer<SharedResource>`，将 `message` 本地化
3. `errorCode` 保持异常类型名或自定义枚举，不做本地化
4. 过渡期：中间件未就绪前，在各 catch 处单独使用 `IStringLocalizer`

> **现有中文硬编码**：`GlobalExceptionHandlerMiddleware.cs` 中已有中文错误消息（如"服务内部错误"）。实施时需注意：以 `en` 为 fallback 会改变现有中文用户的体验。建议将默认语言改为 `zh-CN` 或等中间件改造完成后统一迁移。

## 4. 语言切换流程

```
用户点击语言切换
  → i18n.changeLanguage('zh-CN')
  → react-i18next 加载 /locales/zh-CN/translation.json
  → 所有 useTranslation hook 重新渲染
  → 同步 Mantine locale 设置
  → axios 拦截器将 Accept-Language 写入后续 API 请求
  → 后端读取 Accept-Language，返回中文错误消息
  → 语言偏好存入 localStorage
```

## 5. 如何添加新语言（贡献者指南）

1. 在 `frontend/public/locales/` 下新建语言目录，复制 `en/` 下所有 JSON 文件并翻译
2. 在 `backend/FlowEngine.Resources/` 下添加 `SharedResource.{lang}.resx`
3. 在 `frontend/src/i18n.ts` 的 `supportedLngs` 中添加语言代码
4. 在 `Program.cs` 的 `supportedCultures` 中添加
5. 更新 `LanguageSwitcher` 的选项列表

工作量：约 30 分钟，纯翻译工作。

> **前后端翻译一致性**：前端 key 以 `en/` 下的 JSON 为权威来源，后端 `.resx` 的 key 集合应与其保持同步。CI 可检查两端 key 是否完全覆盖。
>
> **Key 完整性检查**：建议使用 `i18next-parser` 自动提取代码中使用的 key，对比翻译文件是否存在遗漏。CI 中可添加检查：`en` 和 `zh-CN` 翻译文件的 key 集合必须一致。

## 6. 实施阶段

### 阶段一：基础设施搭建

**前端依赖安装**：
```bash
npm install i18next react-i18next i18next-browser-languagedetector i18next-http-backend
```

**后端依赖**：ASP.NET Core 内置支持，无需额外 NuGet 包。

**实施清单**：
- 创建 `frontend/src/i18n.ts` 配置
- 新建 `backend/FlowEngine.Resources/` 项目，配置 `.csproj` 的 `LogicalName`
- 配置 `Program.cs` 本地化中间件 + `AddDataAnnotationsLocalization()`
- 搭建中英翻译文件（所有 namespace 的骨架 JSON + `.resx`）

### 阶段二：前端 UI 迁移

- 创建语言切换组件
- 将各页面/组件的硬编码英文替换为 `t()` 调用
- 按模块逐个迁移：LoginPage → HeaderToolbar → SettingsPage → WorkflowListPage → NodePanel → ParameterPanel → ExecutionPanel → Admin 页面

### 阶段三：后端 API 消息迁移

- 新建统一异常中间件 `GlobalExceptionHandlerMiddleware`
- 将 Controller / Service 中的硬编码错误消息替换为 `IStringLocalizer` 调用
- `errorCode` 保持英文机器码，仅本地化 `message`

### 阶段四：验证

**编译检查**：
- 后端：`dotnet build` + `dotnet test`
- 前端：`npm run build` + `npm run typecheck`

**功能验证**：
- 手动验证：切换到中文后检查各页面和 API 错误消息
- 后端：不同 `Accept-Language` header 下错误响应语言正确

**翻译完整性**：
- 中英 JSON 翻译文件 key 集合一致（可用 `i18next-parser` 自动检测）
- 前端 mock `useTranslation` 的测试用例通过
- 后端 mock `IStringLocalizer` 的测试用例通过

## 7. 未涵盖的领域

| 领域 | 说明 | 建议时机 |
|------|------|----------|
| 日期/时间格式化 | 当前使用 `toLocaleDateString()` 自动跟随浏览器语言 | 注意 `Intl.DateTimeFormat` 与 `i18n.language` 一致 |
| 数字/文件大小格式化 | 当前无格式化函数 | 新增时统一使用 `Intl.NumberFormat` |
| 复数形式 | .resx 和 i18next 均支持，但当前翻译文件未定义复数 key | 需要时再添加 |
| 数据表格（Mantine Table） | 空状态提示、分页文案等 | 随模块迁移补充 |
| 通知/Toast | `notifications.show` 标题和消息 | 随模块迁移补充 |

## 8. 变更记录

| 日期 | 修改人 | 修改内容 | 关联任务 |
|------|--------|----------|----------|
| 2026-07-16 | Agent | 初稿 | i18n 设计 |
| 2026-07-16 | Agent | 评审修复：resx 命名空间/LocalName、errorCode 保留、去除 I18nextProvider、axios 类型安全、zh-CN 匹配、翻译骨架补全、Key 命名规范、测试策略、DataAnnotations 本地化、Mantine locale 同步 | 评审修复 |
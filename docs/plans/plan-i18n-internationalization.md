# i18n 国际化实施计划

> **给执行 agent 的说明：** 推荐使用 `subagent-driven-development` 技能逐任务执行。每步用复选框（`- [ ]`）追踪进度。

**目标：** 为 Flow Engine 添加中英双语支持。前端 UI 和 Controller 错误消息做本地化。运行时引擎错误（`BusinessException`、节点执行错误）和服务层消息保持英文——它们由代码消费，不直接面向用户。

**整体方案：**
- 前端：`react-i18next`，按 namespace 拆分的 JSON 翻译文件，`i18next-http-backend` 懒加载。语言偏好存 `localStorage`
- 后端：`IStringLocalizer<SharedResource>` + `.resx` 资源文件。`RequestLocalizationMiddleware` 读 `Accept-Language` header。API 响应中 `errorCode` 保持英文机器码不变，仅 `message` 本地化

**技术栈：** react-i18next, i18next-http-backend, i18next-browser-languagedetector, ASP.NET Core 本地化 (`IStringLocalizer<T>`), .resx 资源文件

## 全局约束

- **API errorCode 禁止本地化。** `errorCode` 保持英文机器码（如 `"AssembleFailed"`、`"WorkflowNotFound"`），仅 `message` 做本地化
- **语言代码使用 BCP-47：** `en`、`zh-CN`。禁止混用 `zh`、`zh_CN`、`zh-Hans`
- **后端使用 .resx 格式。** 纯 XML，任意文本编辑器可改。文件放项目根目录（与 `SharedResource.cs` 同级），靠 `RootNamespace` 约定匹配类型名，不写显式 `LogicalName`
- **前端用 `resolvedLanguage`。（不是 `language`）** 避免初始化完成前返回 `undefined`
- **`nonExplicitSupportedLngs: true`** 让浏览器语言 `zh` 自动映射到 `zh-CN`
- **翻译 key 命名规范：** `{模块}.{组件}.{用途}`，如 `settings.apiKeys.createKey`
- **不翻译的内容：** 用户创建的工作流名称、节点标签、项目名、日志/审计记录、文件内容。这些由用户输入，保持原样

---

## 文件结构

### 新建文件
```
backend/FlowEngine.Resources/
├── FlowEngine.Resources.csproj      # 类库，无依赖
├── SharedResource.cs                # 标记类
├── SharedResource.resx              # 默认（英文）
└── SharedResource.zh-CN.resx        # 中文

frontend/src/i18n.ts                 # i18next 初始化

frontend/public/locales/{lang}/
├── common.json
├── login.json
├── header.json
├── settings.json
├── workflow.json
├── nodePanel.json
├── parameterPanel.json
├── execution.json
└── admin.json
```

### 修改文件
```
backend/FlowEngine.Host/Program.cs                              # 注册本地化服务
backend/FlowEngine.Host/FlowEngine.Host.csproj                  # 项目引用
backend/FlowEngine.Host/Middlewares/GlobalExceptionHandlerMiddleware.cs  # 改造输出格式 + 本地化
backend/FlowEngine.Host/Controllers/ControllerExtensions.cs     # 无修改（仅包装消息）
backend/FlowEngine.Host/Controllers/WorkflowsController.cs      # 本地化硬编码消息
backend/FlowEngine.Host/Controllers/AiWorkflowsController.cs    # 同上
backend/FlowEngine.Host/Controllers/ExecutionsController.cs     # 同上
backend/FlowEngine.Host/Controllers/UsersController.cs          # 同上
backend/FlowEngine.Host/Controllers/ProjectsController.cs       # 同上
backend/FlowEngine.Host/Controllers/TriggersController.cs       # 同上（按需）
backend/FlowEngine.Runtime/Executor/ErrorStrategyHandler.cs     # 中文 → 英文

frontend/src/main.tsx                                           # import './i18n'
frontend/src/services/api.ts                                   # Accept-Language 拦截器
frontend/src/hooks/AuthContext.tsx                              # 本地化通知
frontend/src/components/Layout/HeaderToolbar.tsx                # 菜单标签
frontend/src/pages/*.tsx                                        # 所有页面
frontend/src/components/Canvas/*.tsx                            # 画布组件
frontend/src/components/NodePanel/*.tsx                         # 节点面板
frontend/src/components/ParameterPanel/*.tsx                    # 参数面板 + 字段
frontend/src/components/ExecutionPanel/*.tsx                    # 执行面板
frontend/src/components/ExecutionView/*.tsx                     # 执行视图
frontend/src/components/WorkflowList/*.tsx                      # 工作流列表
frontend/src/components/CredentialPanel/*.tsx                   # 凭据面板
frontend/src/components/admin/*.tsx                             # 管理组件
frontend/src/components/common/*.tsx                            # 通用组件
```

---

### 任务 1：后端 — 创建 FlowEngine.Resources 项目 + .resx 文件

**涉及文件：**
- 新建: `backend/FlowEngine.Resources/FlowEngine.Resources.csproj`
- 新建: `backend/FlowEngine.Resources/SharedResource.cs`
- 新建: `backend/FlowEngine.Resources/SharedResource.resx`
- 新建: `backend/FlowEngine.Resources/SharedResource.zh-CN.resx`

**产出接口：** `FlowEngine.Resources.SharedResource`（`IStringLocalizer<SharedResource>` 注入用）

- [ ] **步骤 1：创建 .csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>FlowEngine.Resources</RootNamespace>
    <ImplicitUsings>disable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
```

> 不写显式 `LogicalName`。.resx 文件放项目根目录（与 `SharedResource.cs` 同级），SDK 按约定生成 `FlowEngine.Resources.SharedResource.resources`，`IStringLocalizer<SharedResource>` 可直接定位。

- [ ] **步骤 2：创建 `SharedResource.cs`**

```csharp
namespace FlowEngine.Resources;

/// <summary>
/// 共享本地化资源标记类，用于 IStringLocalizer&lt;SharedResource&gt; 依赖注入。
/// </summary>
public class SharedResource;
```

- [ ] **步骤 3：创建默认 .resx（英文）**

`SharedResource.resx`，包含以下 key（覆盖所有 Controller 和 Middleware 会使用的错误消息）：

| key | 英文值 |
|-----|--------|
| `InternalServerError` | An internal error occurred. Please try again later. |
| `RequestNotProcessed` | The request could not be processed. Please check your input and try again. |
| `NotFound` | Not Found |
| `BadRequest` | Bad Request |
| `Forbidden` | Forbidden |
| `Unauthorized` | Unauthorized |
| `ValidationFailed` | Validation failed |
| `WorkflowNotFound` | Workflow not found |
| `NodeNotFound` | Node not found |
| `ExecutionNotFound` | Execution not found |
| `WorkflowNameRequired` | Workflow name is required |
| `NodesRequired` | Nodes must not be empty. |
| `ConnectionsRequired` | Connections must not be empty. |
| `WorkflowIdListRequired` | Workflow ID list must not be empty. |
| `InvalidExpression` | Invalid expression: {0} |
| `CredentialNotFound` | Credential not found |
| `ApiKeyNameRequired` | API key name is required |
| `ApiKeyRevoked` | API key "{0}" has been revoked |
| `ApiKeyCreateFailed` | Failed to create API key |
| `ApiKeyRevokeFailed` | Failed to revoke API key |
| `AssembleFailed` | Assembly failed: {0} |
| `ModifyFailed` | Modification failed: {0} |
| `ExportFailed` | Export failed. Please try again. |
| `ImportFailed` | Import failed. Please check the input. |
| `WebhookPathInUse` | Webhook path "{0}" is already in use. |

```xml
<?xml version="1.0" encoding="utf-8"?>
<root>
  <data name="InternalServerError" xml:space="preserve">
    <value>An internal error occurred. Please try again later.</value>
  </data>
  <data name="RequestNotProcessed" xml:space="preserve">
    <value>The request could not be processed. Please check your input and try again.</value>
  </data>
  <!-- ...其余 key 同上表... -->
</root>
```

- [ ] **步骤 4：创建中文 .resx**

`SharedResource.zh-CN.resx`，key 与默认文件完全一致，value 为中文翻译。

- [ ] **步骤 5：验证编译**

```bash
dotnet build backend/FlowEngine.Resources/FlowEngine.Resources.csproj
```
预期：编译成功。

---

### 任务 2：后端 — 注册本地化管道

**涉及文件：**
- 修改: `backend/FlowEngine.Host/FlowEngine.Host.csproj`
- 修改: `backend/FlowEngine.Host/Program.cs`

**前置依赖：** Task 1（FlowEngine.Resources 项目）

- [ ] **步骤 1：添加项目引用**

`backend/FlowEngine.Host/FlowEngine.Host.csproj`，在已有 `<ItemGroup>` 中添加：

```xml
<ProjectReference Include="..\FlowEngine.Resources\FlowEngine.Resources.csproj" />
```

- [ ] **步骤 2：注册本地化服务**

在 `Program.cs` 的 `builder.Services` 区域添加（`AddControllers` 之前）：

```csharp
using System.Globalization;

// --- 本地化 ---
builder.Services.AddLocalization();

builder.Services.AddControllers()
    .AddDataAnnotationsLocalization();
```

> `AddDataAnnotationsLocalization()` 让 `[Required]`、`[StringLength]` 等 DataAnnotation 验证消息支持本地化。如果当前项目未大量使用 DataAnnotation 验证，此调用无害，保留即可。

- [ ] **步骤 3：配置中间件**

在 `app.UseAuthentication(); app.UseAuthorization();` 之后、`app.MapControllers();` 之前添加：

```csharp
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

- [ ] **步骤 4：验证编译**

```bash
dotnet build backend/FlowEngine.Host/FlowEngine.Host.csproj
```
预期：编译成功。

---

### 任务 3：后端 — 统一异常中间件输出格式 + 本地化

**涉及文件：**
- 修改: `backend/FlowEngine.Host/Middlewares/GlobalExceptionHandlerMiddleware.cs`

**前置依赖：** Task 1、Task 2

**背景：** 当前中间件输出 `{type, title, status, detail, traceId}`，但（1）前端拦截器只读 `message` 不读 `detail`，本地化不会生效；（2）与项目标准格式 `{success, errorCode, message, details}` 不一致。本任务同时修复这两个问题。

- [ ] **步骤 1：改造中间件输出格式**

注入本地化器，将输出格式改为 `{success, errorCode, message, details}`，`errorCode` 从异常类型派生：

```csharp
using System.Diagnostics;
using System.Text.Json;
using FlowEngine.Core.Exceptions;
using FlowEngine.Resources;
using Microsoft.Extensions.Localization;

namespace FlowEngine.Host.Middlewares;

public class GlobalExceptionHandlerMiddleware(
    RequestDelegate next,
    ILogger<GlobalExceptionHandlerMiddleware> logger,
    IStringLocalizer<SharedResource> localizer)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var (status, errorCode) = MapException(exception);
        var traceId = Activity.Current?.Id ?? context.TraceIdentifier;

        if (status >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "未处理异常，traceId={TraceId}", traceId);
        }
        else
        {
            logger.LogWarning(exception, "业务异常 {Status}: {Message}", status, exception.Message);
        }

        // 5xx：对外隐藏内部细节，返回通用提示
        // 4xx：BusinessException/ArgumentException 透传原始消息，其余返回通用提示
        var message = status >= StatusCodes.Status500InternalServerError
            ? localizer["InternalServerError"]
            : exception is BusinessException or ArgumentException
                ? exception.Message
                : localizer["RequestNotProcessed"];

        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json; charset=utf-8";

        var payload = new
        {
            success = false,
            errorCode,
            message,
            details = new { traceId },
        };

        await context.Response.WriteAsync(
            JsonSerializer.Serialize(payload, JsonOptions));
    }

    private static (int status, string errorCode) MapException(Exception exception)
    {
        return exception switch
        {
            PermissionDeniedException => (StatusCodes.Status403Forbidden, "Forbidden"),
            UnauthorizedException => (StatusCodes.Status401Unauthorized, "Unauthorized"),
            NotFoundException => (StatusCodes.Status404NotFound, "NotFound"),
            BusinessException => (StatusCodes.Status400BadRequest, "BadRequest"),
            ArgumentException => (StatusCodes.Status400BadRequest, "BadRequest"),
            InvalidOperationException => (StatusCodes.Status500InternalServerError, "InternalServerError"),
            UnauthorizedAccessException => (StatusCodes.Status403Forbidden, "Forbidden"),
            KeyNotFoundException => (StatusCodes.Status404NotFound, "NotFound"),
            _ => (StatusCodes.Status500InternalServerError, "InternalServerError"),
        };
    }
}
```

> **修改要点**：原来 `MapException` 返回 `(status, title)`，`title` 是英文 HTTP 状态文字（"Not Found"、"Bad Request"）。现在改为 `(status, errorCode)`，与项目统一错误格式匹配。`title` 不再需要，转为 `errorCode`。

- [ ] **步骤 2：更新前端拦截器读取 middleware 响应**

> 需要确认 `frontend/src/services/api.ts` 中的错误处理逻辑能正确解析新的 `{success, errorCode, message, details}` 格式。如果当前只读 `message`，则不需要修改——新格式中消息字段就是 `message`。

- [ ] **步骤 3：验证编译**

```bash
dotnet build backend/FlowEngine.Host/
```
预期：编译成功。

---

### 任务 4：后端 — 本地化所有 Controller 错误消息

**涉及文件：**
- 修改: `backend/FlowEngine.Host/Controllers/WorkflowsController.cs`
- 修改: `backend/FlowEngine.Host/Controllers/AiWorkflowsController.cs`
- 修改: `backend/FlowEngine.Host/Controllers/ExecutionsController.cs`
- 修改: `backend/FlowEngine.Host/Controllers/UsersController.cs`
- 修改: `backend/FlowEngine.Host/Controllers/ProjectsController.cs`
- 修改: `backend/FlowEngine.Host/Controllers/TriggersController.cs`
- 修改: `backend/FlowEngine.Runtime/Executor/ErrorStrategyHandler.cs`
- 搜索并处理: 其他所有抛出 `BusinessException` 或硬编码中/英文字符串的地方

**前置依赖：** Task 3（middleware 格式对齐）

- [ ] **步骤 1：枚举所有硬编码错误消息**

```bash
# Windows 用 Select-String，Linux/macOS 用 rg
Select-String -Pattern 'BadRequestError\("' backend/FlowEngine.Host/Controllers/
Select-String -Pattern 'this\.BadRequest\(' backend/FlowEngine.Host/Controllers/
Select-String -Pattern 'return BadRequest\(' backend/FlowEngine.Host/Controllers/
Select-String -Pattern 'errorCode = "' backend/FlowEngine.Host/Controllers/
```

为每处硬编码消息在 `SharedResource.resx` 中添加对应 key（如果 Task 1 的初始列表已包含则跳过），然后用 `localizer["KeyName"]` 替换。

**已知需要替换的位置：**
- `WorkflowsController.cs` L101：`"Nodes 不能为空。"` → `localizer["NodesRequired"]`
- `WorkflowsController.cs` L106：`"Connections 不能为空。"` → `localizer["ConnectionsRequired"]`
- `WorkflowsController.cs` L167：`"工作流 ID 列表不能为空。"` → `localizer["WorkflowIdListRequired"]`

- [ ] **步骤 2：本地化 WorkflowsController**

注入 `IStringLocalizer<SharedResource>`：

```csharp
public class WorkflowsController(
    IWorkflowService workflowService,
    IWorkflowExportService exportService,
    IWorkflowImportService importService,
    IDryRunService dryRunService,
    IStringLocalizer<SharedResource> localizer) : ControllerBase
```

将 `ExportBatch` 的 `InvalidOperationException` catch 改为本地化消息。

- [ ] **步骤 3：本地化 AiWorkflowsController**

注入本地化器，在 catch 块中使用。`errorCode` 保持英文机器码，仅 `message` 本地化：

```csharp
catch (BusinessException ex)
{
    return BadRequest(new
    {
        success = false,
        errorCode = "AssembleFailed",
        message = localizer["AssembleFailed", ex.Message],
    });
}
```

同理处理 `Modify` 方法的 catch。

- [ ] **步骤 4：处理 ErrorStrategyHandler 的中文硬编码**

`backend/FlowEngine.Runtime/Executor/ErrorStrategyHandler.cs` 中有中文消息：
```csharp
Message = "等待输入超时。"  // CreateInputTimeoutResult
Message = "节点执行失败。"  // CreateContinueResult
```

这些消息属于运行时引擎层，通过 `NodeExecutionResult` 返回给工作流输出，不通过 API 返回。当前方案：**改为英文**（与整个项目 en fallback 保持一致），不做完整的 i18n 注入。

```csharp
// CreateInputTimeoutResult
Code = "InputTimeout",
Message = "Input timed out.",

// CreateContinueResult
Code = "NodeError",
Message = "Node execution failed.",
```

> 未来如果需要本地化运行时消息，可以在 `NodeError` 中添加语言字段或通过 Expression 引擎查找翻译，本阶段不涉及。

- [ ] **步骤 5：处理 BusinessException 抛出点**

```bash
# 找到所有 throw new BusinessException(...) 的位置
Select-String -Pattern 'throw new BusinessException\(' backend/FlowEngine.Application/
```

这些消息目前已经是英文（如 `"Webhook path '{0}' is already in use."`），且抛出在 Application 层（不引用 `FlowEngine.Resources`）。**保持英文不变**，中间件透传。在抛出点加 TODO 注释：

```csharp
// TODO(i18n): 将 BusinessException 消息改为注入 IStringLocalizer 后本地化
throw new BusinessException($"Webhook path '{path}' is already in use.");
```

> 当前阶段：Controller 层 + 中间件消息完整本地化。服务层 `BusinessException` 英文透传——`errorCode` 已足够前端做分支判断，英文消息也比空值好。

- [ ] **步骤 6：更新测试中的错误消息断言**

搜索测试代码中硬编码的错误消息文本，替换为对 `errorCode` 的断言或 mock localizer：

```bash
Select-String -Pattern '"Workflow not found"|"Nodes 不能为空"|"等待输入超时"' tests/
```

将每条匹配改为：
- ✅ `Assert.Equal("WorkflowNotFound", result.ErrorCode)`（对 errorCode 断言）
- ❌ `Assert.Equal("Workflow not found", result.Message)`（对本地化消息断言会因语言不同而失败）

- [ ] **步骤 7：验证编译 + 测试**

```bash
dotnet build backend/
dotnet test tests/
```
预期：编译成功，所有测试通过。

---

### 任务 5：前端 — i18n 基础设施 + 翻译 JSON 文件

**涉及文件：**
- 新建: `frontend/src/i18n.ts`
- 新建: 9 个 `frontend/public/locales/en/*.json`
- 新建: 9 个 `frontend/public/locales/zh-CN/*.json`
- 修改: `frontend/src/main.tsx`

**前置依赖：** 无

- [ ] **步骤 1：安装 npm 依赖**

```bash
cd frontend
npm install i18next react-i18next i18next-browser-languagedetector i18next-http-backend
```

- [ ] **步骤 2：创建 `frontend/src/i18n.ts`**

```typescript
import i18n from 'i18next';
import { initReactI18next } from 'react-i18next';
import LanguageDetector from 'i18next-browser-languagedetector';
import Backend from 'i18next-http-backend';

i18n
  .use(Backend)
  .use(LanguageDetector)
  .use(initReactI18next)  // 自动注入 React Context，无需手动包 I18nextProvider
  .init({
    fallbackLng: 'en',
    supportedLngs: ['en', 'zh-CN'],
    nonExplicitSupportedLngs: true,  // 浏览器 'zh' 自动映射到 'zh-CN'
    ns: [
      'common', 'login', 'header', 'settings',
      'workflow', 'nodePanel', 'parameterPanel',
      'execution', 'admin',
    ],
    defaultNS: 'common',
    interpolation: { escapeValue: false },
    backend: {
      loadPath: '/locales/{{lng}}/{{ns}}.json',
    },
    detection: {
      order: ['localStorage', 'navigator'],
      caches: ['localStorage'],
      lookupLocalStorage: 'i18nextLng',
    },
    react: {
      useSuspense: false,  // 首次加载不触发 Suspense，避免白屏
    },
  })
  .catch((err) => {
    console.error('i18n 初始化失败:', err);
  });

export default i18n;
```

> **关于 `backend.loadPath`**：Vite dev server 下 `/locales/...` 指向 `public/` 目录。生产部署时如果前端资源不在根路径，需调整此路径。当前项目前后端在同域部署，`public/` 目录映射到 `/`，无需特殊配置。

> **关于 9 个 namespace 的加载**：`i18next-http-backend` 默认在初始化时同时加载全部 9 个 namespace，会发起 9 个 HTTP 请求。当前项目规模下这是可接受的。未来如果 namespace 数量增长，可考虑 `lazy: true` 按需加载或合并 namespace 文件。

- [ ] **步骤 3：更新 `main.tsx`**

在文件最顶部添加 import（必须在 `MantineProvider` 和 `App` 之前）：

```typescript
import './i18n';  // i18n 初始化 — 必须放在最前面，确保所有 useTranslation 使用前已就绪
```

检查当前 `main.tsx` 是否包含 `<I18nextProvider>` 包裹层。如果有，移除它——`initReactI18next` 已自动注入 React Context。

- [ ] **步骤 4：创建英文翻译 JSON 文件**

在 `frontend/public/locales/en/` 下创建 9 个文件。完整内容见设计文档 `docs/designs/2026-07-16-i18n-internationalization.md` 第 2.6 节。

| 文件 | 说明 |
|------|------|
| `common.json` | save, cancel, delete, create, edit, loading, error, noData, confirmDelete, confirmDiscard, loggedOut, sessionExpired |
| `login.json` | title, subtitle, email, password, signIn, failed, unexpectedError |
| `header.json` | workflows, executions, settings, admin, help, signOut |
| `settings.json` | 用户信息字段、API Key 管理完整（含状态徽标、模态框） |
| `workflow.json` | title, new, search, noWorkflows, import, export, confirmDelete 及通知消息 |
| `nodePanel.json` | title, search, noResults |
| `parameterPanel.json` | title, noSelection |
| `execution.json` | run, stop, status 状态值 (idle/running/completed/failed/cancelled), output, error, duration, 时间戳 |
| `admin.json` | 四个管理页面的标题、按钮、确认消息 |

- [ ] **步骤 5：创建中文翻译 JSON 文件**

复制 `en/` 下所有 9 个文件到 `frontend/public/locales/zh-CN/`，将所有 value 翻译为中文。key 必须完全一致。

- [ ] **步骤 6：验证类型检查**

```bash
cd frontend && npm run typecheck
```
预期：无类型错误。

---

### 任务 6：前端 — LanguageSwitcher + API 拦截器 + Mantine 语言同步

**涉及文件：**
- 新建: `frontend/src/components/common/LanguageSwitcher.tsx`
- 修改: `frontend/src/services/api.ts`
- 修改: `frontend/src/components/Layout/HeaderToolbar.tsx`

**前置依赖：** Task 5（i18next 初始化）

- [ ] **步骤 1：创建 LanguageSwitcher 组件**

`frontend/src/components/common/LanguageSwitcher.tsx`：

```tsx
import { Select } from '@mantine/core';
import { useTranslation } from 'react-i18next';

export function LanguageSwitcher() {
  const { i18n } = useTranslation();

  const handleChange = (val: string | null) => {
    if (!val) return;
    i18n.changeLanguage(val);
  };

  return (
    <Select
      size="xs"
      w={110}
      value={i18n.resolvedLanguage}
      onChange={handleChange}
      data={[
        { value: 'en', label: 'English' },
        { value: 'zh-CN', label: '中文' },
      ]}
    />
  );
}
```

- [ ] **步骤 2：在 LanguageSwitcher 中同步 Mantine locale**

当用户切换语言时，需要同步更新 Mantine 日期组件（`@mantine/dates`）的 locale。在 `LanguageSwitcher` 中添加 `useEffect`：

```tsx
import { useEffect } from 'react';
// 如果使用了 @mantine/dates，需要安装 dayjs 并 import locale
// import dayjs from 'dayjs';
// import 'dayjs/locale/zh-cn';
// import 'dayjs/locale/en';

export function LanguageSwitcher() {
  const { i18n } = useTranslation();

  useEffect(() => {
    // 同步 dayjs locale（用于 Mantine DatePicker 等）
    // const langMap: Record<string, string> = { en: 'en', 'zh-CN': 'zh-cn' };
    // dayjs.locale(langMap[i18n.resolvedLanguage ?? 'en']);
    // 同步 Mantine DatesProvider locale（如果组件使用 DatesProvider）
    // 当前未使用日期组件，预留接口
  }, [i18n.resolvedLanguage]);

  // ...其余代码同上
}
```

> 当前项目未使用 `@mantine/dates`，所以 locale 同步是防御性代码。未来引入日期选择器时需激活此逻辑。

- [ ] **步骤 3：将 LanguageSwitcher 加入 HeaderToolbar**

`frontend/src/components/Layout/HeaderToolbar.tsx`：

```tsx
// 添加 import
import { LanguageSwitcher } from '../common/LanguageSwitcher.tsx';

// 在工具栏右侧区域（settings / signOut 之前）添加：
<LanguageSwitcher />
```

- [ ] **步骤 4：添加 API 拦截器**

在 `frontend/src/services/api.ts` 中找到 axios 实例定义处，添加请求拦截器：

```typescript
import i18n from '../i18n.ts';

// 在 axios 实例定义之后添加，用 rg 'const (api|http|client) =' 确认变量名
api.interceptors.request.use((config) => {
  config.headers.set('Accept-Language', i18n.resolvedLanguage ?? 'en');
  return config;
});
```

> 用 `Select-String -Pattern 'const (api|http|client) =' frontend/src/services/api.ts` 确认 axios 实例的变量名。

- [ ] **步骤 5：验证编译**

```bash
cd frontend && npm run typecheck && npm run build
```
预期：编译成功。

---

### 任务 7：前端 — 迁移 LoginPage + HeaderToolbar + AuthContext

**涉及文件：**
- 修改: `frontend/src/pages/LoginPage.tsx`
- 修改: `frontend/src/components/Layout/HeaderToolbar.tsx`
- 修改: `frontend/src/hooks/AuthContext.tsx`

**前置依赖：** Task 5、Task 6

- [ ] **步骤 1：迁移 LoginPage**

引入 `useTranslation`，将硬编码英文全部替换为 `t()` 调用：

```tsx
import { useTranslation } from 'react-i18next';

export function LoginPage() {
  const { t } = useTranslation('login');
  // ...

  return (
    <Title order={3}>{t('title')}</Title>
    <Text size="sm" c="dimmed">{t('subtitle')}</Text>
    <TextInput label={t('email')} placeholder={t('email')} ... />
    <PasswordInput label={t('password')} placeholder={t('password')} ... />
    <Button ...>{t('signIn')}</Button>
  );
}
```

错误消息：
```tsx
setError(result.error ?? t('failed'));
onError: () => setError(t('unexpectedError')),
```

- [ ] **步骤 2：迁移 HeaderToolbar**

引入 `useTranslation('header')`，替换所有导航标签和按钮文字。

- [ ] **步骤 3：迁移 AuthContext.tsx 通知消息**

```tsx
const { t } = useTranslation('common');
notifications.show({ title: t('loggedOut'), message: t('sessionExpired'), color: 'blue' });
```

- [ ] **步骤 4：修复前端测试中的硬编码文本断言**

搜索测试代码中引用了迁移过文本的位置：

```bash
Select-String -Pattern "'Sign In'|'Email'|'Password'|'Workflows'" frontend/src/**/__tests__/*.test.tsx
```

针对每条匹配：
- 如果组件已改为 `t()`，测试不能再用 `getByText('Sign In')`，应改为：
  - 用 `data-testid` 属性定位
  - 或用 `getByRole('button', { name: /sign in/i })`（不依赖翻译文本）
  - 或在测试 wrapper 中 mock i18n 返回固定英文文本

- [ ] **步骤 5：验证编译 + 测试**

```bash
cd frontend && npm run typecheck && npm test -- --run
```
预期：无错误，所有测试通过。

---

### 任务 8：前端 — 迁移 SettingsPage

**涉及文件：**
- 修改: `frontend/src/pages/SettingsPage.tsx`

**前置依赖：** Task 5、Task 6

- [ ] **步骤 1：替换所有硬编码字符串**

引入 `useTranslation('settings')`，替换：
- 页面标题、区域标题
- 用户信息字段标签
- API Key 管理区的表头、状态徽标文字、模态框标题、按钮、提示消息
- 所有 `notifications.show` 的 title 和 message

- [ ] **步骤 2：处理日期格式化**

`SettingsPage.tsx` 中 `formatDate` 函数使用 `toLocaleDateString()`，它跟随浏览器 locale 而非 i18n 语言选择。这可能导致 UI 语言是中文但日期格式仍显示英文。

当前阶段保留 `toLocaleDateString()`（自动跟随操作系统 locale，大多数字号一致）。未来如需精确匹配 i18n 语言，可引入 `date-fns`：

```tsx
import { format } from 'date-fns';
import { zhCN, enUS } from 'date-fns/locale';

const localeMap: Record<string, Locale> = { en: enUS, 'zh-CN': zhCN };
format(date, 'PPP', { locale: localeMap[i18n.language] });
```

- [ ] **步骤 3：验证编译**

```bash
cd frontend && npm run typecheck
```
预期：无错误。

---

### 任务 9a：前端 — 迁移 WorkflowList + WorkflowEditorPage + Canvas

**涉及文件：**
- 修改: `frontend/src/components/WorkflowList/WorkflowListPage.tsx`
- 修改: `frontend/src/components/WorkflowList/ProjectFilter.tsx`
- 修改: `frontend/src/pages/WorkflowEditorPage.tsx`
- 修改: `frontend/src/components/Canvas/WorkflowCanvas.tsx`
- 修改: `frontend/src/components/Canvas/CanvasToolbar.tsx`
- 修改: `frontend/src/components/Canvas/CustomNode.tsx`
- 修改: `frontend/src/components/Canvas/CustomEdge.tsx`

**前置依赖：** Task 5、Task 6

- [ ] **步骤 1：迁移 WorkflowListPage + ProjectFilter**

引入 `useTranslation('workflow')`。替换：
- 页面标题、按钮文字、搜索占位符、空状态提示、删除确认弹窗
- 所有 `notifications.show` 的 title 和 message（包括导入导出、删除、错误等）

翻译 key 示例：
```json
"deleted": "Deleted",
"deletedMessage": "Workflow \"{{name}}\" deleted.",
"exported": "Exported",
"exportedMessage": "Workflow \"{{name}}\" exported.",
"imported": "Imported",
"importedMessage": "Workflow imported.",
"confirmDelete": "Delete workflow \"{{name}}\"?",
"confirmDeleteWarning": "This cannot be undone."
```

- [ ] **步骤 2：迁移 WorkflowCanvas + CanvasToolbar**

- CanvasToolbar：工具按钮标签、缩放百分比、通知消息
- WorkflowCanvas：`notifications.show` 字符串（当前有中文 `"节点已复制到剪贴板"` → 用 `t('nodeCopied')`）
- CustomNode / CustomEdge：检查是否有静态标签

- [ ] **步骤 3：迁移 WorkflowEditorPage**

替换通知消息（确认/驳回/激活等反馈）：
```tsx
notifications.show({ title: t('activated'), message: t('activationMessage'), color: 'green' });
notifications.show({ title: t('rejected'), message: t('rejectionMessage'), color: 'orange' });
```

- [ ] **步骤 4：验证编译**

```bash
cd frontend && npm run typecheck
```
预期：无错误。

---

### 任务 9b：前端 — 迁移 NodePanel + ParameterPanel + 字段组件

**涉及文件：**
- 修改: `frontend/src/components/NodePanel/NodePanel.tsx`
- 修改: `frontend/src/components/NodePanel/NodeCard.tsx`
- 修改: `frontend/src/components/ParameterPanel/ParameterPanel.tsx`
- 修改: `frontend/src/components/ParameterPanel/FieldResolver.tsx`
- 修改: `frontend/src/components/ParameterPanel/DiffPanel.tsx`
- 修改: `frontend/src/components/ParameterPanel/TriggerConfig.tsx`
- 修改: `frontend/src/components/ParameterPanel/ValidationChecklistModal.tsx`
- 修改: `frontend/src/components/ParameterPanel/fields/*.tsx`（19 个字段）

**前置依赖：** Task 5、Task 6

- [ ] **步骤 1：迁移 NodePanel + NodeCard**

引入 `useTranslation('nodePanel')`：
- `t('title')`、`t('search')`、`t('noResults')`

- [ ] **步骤 2：迁移 ParameterPanel + FieldResolver + DiffPanel**

引入 `useTranslation('parameterPanel')`：
- `t('title')`、`t('noSelection')`
- DiffPanel 中的静态标签

- [ ] **步骤 3：迁移字段组件**

19 个字段组件（`StringField`、`NumberField`、`BooleanField`、`CodeField`、`JsonField`、`ArrayField`、`KeyValueField`、`OptionsField`、`ButtonGroupField`、`CronBuilder`、`ExpressionField`、`SecretField`、`ResourceField`、`FileField`、`CredentialField`、`TextAreaField`、`InfoTooltip`、`FileField` 等）。

大部分字段的标签来自 `ParameterDefinition.name`（动态数据），不需要翻译。只需要处理：
- 静态标签 / 工具提示
- `InfoTooltip.tsx` 中的静态文案
- `FileField.tsx` 中的 `notifications.show` 消息
- `CredentialField.tsx` 中的 `notifications.show` 消息

- [ ] **步骤 4：迁移 TriggerConfig + ValidationChecklistModal**

- TriggerConfig：通知消息的 title 和 message
- ValidationChecklistModal：弹窗标题、按钮文字

- [ ] **步骤 5：验证编译**

```bash
cd frontend && npm run typecheck
```
预期：无错误。

---

### 任务 9c：前端 — 迁移 CredentialPanel + 通用组件

**涉及文件：**
- 修改: `frontend/src/components/CredentialPanel/CredentialListModal.tsx`
- 修改: `frontend/src/components/common/NodeIcon.tsx`
- 修改: `frontend/src/components/common/RequireRole.tsx`

**前置依赖：** Task 5、Task 6

- [ ] **步骤 1：迁移 CredentialListModal**

替换模态框标题、按钮文字、`notifications.show` 消息。

- [ ] **步骤 2：迁移通用组件**

- `NodeIcon.tsx`：检查是否有静态 tooltip 文字
- `RequireRole.tsx`：检查是否有权限不足提示文字

- [ ] **步骤 3：验证编译**

```bash
cd frontend && npm run typecheck
```
预期：无错误。

---

### 任务 10a：前端 — 迁移 Execution 组件

**涉及文件：**
- 修改: `frontend/src/components/ExecutionPanel/ExecutionPanel.tsx`
- 修改: `frontend/src/components/ExecutionPanel/ExecutionButton.tsx`
- 修改: `frontend/src/components/ExecutionPanel/StepItem.tsx`
- 修改: `frontend/src/components/ExecutionPanel/NodeOutputList.tsx`
- 修改: `frontend/src/components/ExecutionPanel/CodeViewer.tsx`
- 修改: `frontend/src/components/ExecutionView/AgentExecutionView.tsx`
- 修改: `frontend/src/components/ExecutionView/LLMThinkingView.tsx`
- 修改: `frontend/src/components/ExecutionView/ToolCallChain.tsx`
- 修改: `frontend/src/pages/ExecutionHistoryPage.tsx`

**前置依赖：** Task 5、Task 6

- [ ] **步骤 1：迁移 ExecutionPanel 组件**

引入 `useTranslation('execution')`：
- 面板标题、运行/停止按钮、状态标签（运行中/已完成/已失败/已取消）
- 空状态提示、输出/错误/持续时间标签
- 时间戳标题

- [ ] **步骤 2：迁移 ExecutionView 组件**

- `AgentExecutionView.tsx`：替换硬编码标签（"Thinking..."、"Tool calls"、"Agent output" 等）
- `LLMThinkingView.tsx`：替换静态文案
- `ToolCallChain.tsx`：替换静态标签

- [ ] **步骤 3：迁移 ExecutionHistoryPage**

替换页面标题、筛选标签、空状态提示、表格表头。

- [ ] **步骤 4：验证编译**

```bash
cd frontend && npm run typecheck
```
预期：无错误。

---

### 任务 10b：前端 — 迁移 Admin 页面 + HelpPage

**涉及文件：**
- 修改: `frontend/src/pages/AdminUsersPage.tsx`
- 修改: `frontend/src/pages/AdminProjectsPage.tsx`
- 修改: `frontend/src/pages/AdminFilesPage.tsx`
- 修改: `frontend/src/pages/AdminAuditPage.tsx`
- 修改: `frontend/src/pages/HelpPage.tsx`
- 修改: `frontend/src/components/admin/RoleAssignModal.tsx`
- 修改: `frontend/src/components/admin/AuditDetailDrawer.tsx`

**前置依赖：** Task 5、Task 6

- [ ] **步骤 1：迁移 Admin 页面**

引入 `useTranslation('admin')`。四个页面统一替换：
- 页面标题、新增/编辑/删除按钮、确认弹窗
- 所有 `notifications.show` 的 title 和 message

`AdminFilesPage.tsx` 通知消息示例：
```json
"uploaded": "已上传",
"uploadedMessage": "「{{name}}」已上传。",
"deleted": "已删除",
"deletedMessage": "「{{name}}」已删除。",
"selectProjectFirst": "请先选择项目后再上传文件。"
```

`AdminProjectsPage.tsx` 通知消息示例：
```json
"updated": "已更新",
"updatedMessage": "项目「{{name}}」已更新。",
"created": "已创建",
"createdMessage": "项目「{{name}}」已创建。"
```

- [ ] **步骤 2：迁移 RoleAssignModal + AuditDetailDrawer**

替换弹窗标题、保存按钮、通知消息。

- [ ] **步骤 3：迁移 HelpPage**

替换页面标题和所有静态说明文字。

- [ ] **步骤 4：验证编译**

```bash
cd frontend && npm run typecheck && npm run build
```
预期：编译成功。

---

### 任务 11：全量验证

**前置依赖：** 所有 Task 1-10b

- [ ] **步骤 1：后端全量编译 + 测试**

```bash
dotnet build backend/
dotnet test tests/
```
预期：所有项目编译成功，全部测试通过。

- [ ] **步骤 2：前端全量编译 + 测试**

```bash
cd frontend
npm run typecheck
npm run build
npm test -- --run
```
预期：无类型错误，构建成功，全部测试通过。

- [ ] **步骤 3：翻译完整性检查**

检查文件列表一致性：
```bash
Compare-Object (Get-ChildItem frontend/public/locales/en/*.json | % Name) `
             (Get-ChildItem frontend/public/locales/zh-CN/*.json | % Name)
```
预期：无差异（文件列表完全一致）。

递归检查 key 一致性（使用 jq 或以下 PowerShell 脚本）：

```powershell
# 递归提取 JSON 中所有叶子 key 的路径
function Get-LeafKeys($obj, $prefix = '') {
  $keys = @()
  foreach ($prop in $obj.PSObject.Properties) {
    $path = if ($prefix) { "$prefix.$($prop.Name)" } else { $prop.Name }
    if ($prop.Value -is [PSCustomObject]) {
      $keys += Get-LeafKeys $prop.Value $path
    } else {
      $keys += $path
    }
  }
  return $keys
}

$anyDiff = $false
foreach ($f in Get-ChildItem frontend/public/locales/en/*.json) {
  $en = Get-Content $f.FullName | ConvertFrom-Json
  $zh = Get-Content "frontend/public/locales/zh-CN/$($f.Name)" | ConvertFrom-Json
  $enKeys = Get-LeafKeys $en | Sort-Object
  $zhKeys = Get-LeafKeys $zh | Sort-Object
  $diff = Compare-Object $enKeys $zhKeys
  if ($diff) { $anyDiff = $true; Write-Host "key 差异 ($($f.Name)): $diff" }
}
if (-not $anyDiff) { Write-Host "所有翻译文件 key 完全一致。" }
```

预期：所有中英文件 key 完全一致。

- [ ] **步骤 4：翻译 key 去重检查**

确保跨 namespace 没有意外重复的 key（后加载的 namespace 会覆盖先加载的）：

```powershell
$allKeys = @{}
foreach ($f in Get-ChildItem frontend/public/locales/en/*.json) {
  $content = Get-Content $f.FullName | ConvertFrom-Json
  $keys = Get-LeafKeys $content
  foreach ($k in $keys) {
    if ($allKeys.ContainsKey($k)) {
      Write-Host "警告: key '$k' 在 $($f.Name) 和 $($allKeys[$k]) 中重复"
    }
    $allKeys[$k] = $f.Name
  }
}
```

- [ ] **步骤 5：i18next-parser 集成（可选）**

安装并运行 `i18next-parser` 自动扫描前端代码中使用的 key，与翻译文件对比：

```bash
cd frontend
npx i18next-parser --config i18next-parser.config.ts 2>$null
```

> 如果 `i18next-parser` 未安装，此步骤可跳过。将来在 CI 中添加翻译完整性检查时再引入。

- [ ] **步骤 6：手动冒烟测试**

1. 启动应用（Host `dotnet run` + 前端 `npm run dev`）
2. 验证默认界面为英文
3. 通过 LanguageSwitcher 切换到中文
4. 验证所有已迁移页面显示中文
5. 验证登录页面在错误凭据下显示中文错误提示
6. 切换回英文，刷新页面，验证语言偏好已持久化

- [ ] **步骤 7：后端本地化冒烟测试**

使用 REST 客户端测试不同 `Accept-Language` 下的错误响应：

```bash
# 中文
curl -s -H "Accept-Language: zh-CN" -H "Authorization: Bearer $TOKEN" `
  http://localhost:5000/api/v1/workflows/00000000-0000-0000-0000-000000000000 `
  | ConvertFrom-Json | Select-Object errorCode, message

# 英文（无 header 默认 en）
curl -s -H "Authorization: Bearer $TOKEN" `
  http://localhost:5000/api/v1/workflows/00000000-0000-0000-0000-000000000000 `
  | ConvertFrom-Json | Select-Object errorCode, message
```

预期：
- `errorCode` 相同（如 `"NotFound"`）
- `message` 不同（中文 vs 英文）

- [ ] **步骤 8：确认无遗留硬编码字符串**

```bash
# 扫描前端 JSX 中可能遗漏的硬编码英文字符串（非 t() 调用的纯字符串）
Select-String -Pattern '(?<!t\()"[A-Z][a-z]+ [a-z]+"|"[A-Z][a-z]+:"' `
  frontend/src/pages/*.tsx frontend/src/components/**/*.tsx
```

> 此扫描会返回一些误报（如动态数据、URL 路径等），人工逐条确认。

---

## 自审清单

- [ ] **设计覆盖：** 设计文档 `docs/designs/2026-07-16-i18n-internationalization.md` 所有章节对应到的任务：

| 设计章节 | 对应任务 |
|----------|----------|
| §2.1 i18n 初始化 | Task 5 |
| §2.2 main.tsx 注入 | Task 5 |
| §2.3 组件用法 | Task 7-10b |
| §2.4 LanguageSwitcher | Task 6 |
| §2.5 API 拦截器 | Task 6 |
| §2.6 翻译文件结构 | Task 5 |
| §3.1 资源项目 | Task 1 |
| §3.2 .resx 文件 | Task 1 |
| §3.3 RequestLocalization | Task 2 |
| §3.4 控制器用法 | Task 3, 4 |
| §3.5 错误响应 | Task 3 |
| §4 语言切换流程 | Task 6 |
| §5 贡献者指南 | Task 5（可扩展的文件结构） |
| §6 实施阶段 | 全部任务 |
| §7 未涵盖领域 | 记录在任务中 |

- [ ] **无占位符：** 无"TBD"、"TODO"（除代码中的 `TODO(i18n)` 注释外）、"implement later" 等。
- [ ] **类型一致性：** 前端统一用 `resolvedLanguage`，后端统一用 `localizer["Key"]`，`errorCode` 保持英文。
- [ ] **验证完整性：** 每个任务都有编译/验证步骤。Task 11 覆盖全量验证。
- [ ] **测试断言修复：** Task 4 Step 6 和 Task 7 Step 4 已包含测试修复步骤。
- [ ] **翻译 key 命名：** 统一 `{module}.{component}.{purpose}`，所有 key 在 en/zh-CN 间一致。
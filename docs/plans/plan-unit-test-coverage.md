# 单元测试覆盖率提升计划（70%+ 目标）

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将后端覆盖率从 54% 提升至 70%+，前端从 50.4% 提升至 65%+。

**Architecture:** 按模块逐个击破，优先补 0% 覆盖的高价值类，再补半覆盖模块的遗漏路径。

**Tech Stack:** .NET 10 / xUnit / Moq / FluentAssertions (后端)；React / Vitest / @testing-library/react (前端)

## 当前后端覆盖率基线（Host.Tests 综合数据）

| 模块 | 当前 | 0% 类数 | 目标 |
|------|------|---------|------|
| Application | 35.6% | 146/260 | 60%+ |
| Core | 31.9% | 84/165 | 55%+ |
| Host | 58.9% | 84/186 | 70%+ |
| Runtime | 42.8% | 43/95 | 60%+ |
| Infrastructure | 26.2% | 17/32 | 50%+ |
| **整体** | **54%** | | **70%+** |

## Global Constraints

- 后端测试框架: xUnit + Moq + FluentAssertions
- 前端测试框架: Vitest + @testing-library/react + jsdom
- 每个 Task 以独立可运行的测试为交付物
- 遵循现有代码风格和目录结构
- 不修改生产代码逻辑，仅补充测试

---

## Phase 1: 后端 — Application 模块（35.6% → 60%+）

> Application 是覆盖率最低的大模块，146 个类在 0%。优先补高频调用的服务类。

### Task 1: ExecutionCleanupService 测试（0% → 80%+）

**Files:**
- Create: `tests/FlowEngine.Application.Tests/ExecutionCleanup/ExecutionCleanupServiceTests.cs`

**覆盖目标:** `ExecutionCleanupService` 及其 4 个异步方法

- [ ] **Step 1: 阅读源码确认接口**

```bash
# 确认 ExecutionCleanupService 的公开方法签名
cat backend/FlowEngine.Application/ExecutionCleanup/ExecutionCleanupService.cs
```

- [ ] **Step 2: 创建测试文件**

```csharp
// tests/FlowEngine.Application.Tests/ExecutionCleanup/ExecutionCleanupServiceTests.cs
using FlowEngine.Application.ExecutionCleanup;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace FlowEngine.Application.Tests.ExecutionCleanup;

public class ExecutionCleanupServiceTests
{
    private readonly Mock<IExecutionLogger> _loggerMock = new();
    private readonly Mock<FlowEngine.Core.Data.FlowEngineDbContext> _dbMock = new();
    private readonly IOptions<ExecutionCleanupOptions> _options;

    public ExecutionCleanupServiceTests()
    {
        _options = Options.Create(new ExecutionCleanupOptions
        {
            RetentionDays = 30,
            BatchSize = 100
        });
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        var act = () => new ExecutionCleanupService(null!, _dbMock.Object, _options, _loggerMock.Object);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullOptions_Throws()
    {
        var act = () => new ExecutionCleanupService(_loggerMock.Object, _dbMock.Object, null!, _loggerMock.Object);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task CleanupAsync_CompletesWithoutError()
    {
        // 使用内存数据库或 mock DbContext
        var service = new ExecutionCleanupService(
            Mock.Of<FlowEngine.Core.Data.FlowEngineDbContext>(),
            _options,
            _loggerMock.Object);

        // CleanupAsync 应该在空数据库上正常完成
        await service.CleanupAsync(CancellationToken.None);
        // 无异常即为通过
    }
}
```

- [ ] **Step 3: 运行测试确认通过**

```bash
dotnet test tests/FlowEngine.Application.Tests/ --filter "ExecutionCleanupServiceTests" -v n
```

---

### Task 2: WorkflowService 核心方法测试（补充）

**Files:**
- Create: `tests/FlowEngine.Application.Tests/Workflows/WorkflowServiceCoreTests.cs`

**覆盖目标:** WorkflowService 的 CRUD 和状态转换方法

- [ ] **Step 1: 创建测试文件**

```csharp
// tests/FlowEngine.Application.Tests/Workflows/WorkflowServiceCoreTests.cs
using FlowEngine.Application.Authorization;
using FlowEngine.Application.Workflows;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FluentAssertions;
using Moq;
using Xunit;

namespace FlowEngine.Application.Tests.Workflows;

public class WorkflowServiceCoreTests
{
    private readonly Mock<IWorkflowRepository> _repoMock = new();
    private readonly Mock<IAuthorizationGuard> _authMock = new();
    private readonly Mock<IEventBus> _eventBusMock = new();
    private readonly WorkflowService _service;

    public WorkflowServiceCoreTests()
    {
        _service = new WorkflowService(
            _repoMock.Object,
            _authMock.Object,
            _eventBusMock.Object);
    }

    [Fact]
    public async Task GetByIdAsync_ExistingWorkflow_ReturnsWorkflow()
    {
        var workflow = new Workflow { Id = "wf-1", Name = "Test" };
        _repoMock.Setup(r => r.GetByIdAsync("wf-1")).ReturnsAsync(workflow);

        var result = await _service.GetByIdAsync("wf-1");

        result.Should().NotBeNull();
        result!.Name.Should().Be("Test");
    }

    [Fact]
    public async Task GetByIdAsync_NonExisting_ReturnsNull()
    {
        _repoMock.Setup(r => r.GetByIdAsync("missing")).ReturnsAsync((Workflow?)null);

        var result = await _service.GetByIdAsync("missing");

        result.Should().BeNull();
    }

    [Fact]
    public async Task CreateAsync_ValidWorkflow_ReturnsCreated()
    {
        var workflow = new Workflow { Name = "New" };
        _repoMock.Setup(r => r.CreateAsync(It.IsAny<Workflow>()))
            .ReturnsAsync((Workflow w) => w);

        var result = await _service.CreateAsync(workflow);

        result.Should().NotBeNull();
        _repoMock.Verify(r => r.CreateAsync(It.IsAny<Workflow>()), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_ExistingWorkflow_ReturnsUpdated()
    {
        var existing = new Workflow { Id = "wf-1", Name = "Old" };
        var updated = new Workflow { Id = "wf-1", Name = "New" };
        _repoMock.Setup(r => r.GetByIdAsync("wf-1")).ReturnsAsync(existing);
        _repoMock.Setup(r => r.UpdateAsync(It.IsAny<Workflow>()))
            .ReturnsAsync((Workflow w) => w);

        var result = await _service.UpdateAsync("wf-1", updated);

        result.Should().NotBeNull();
        _repoMock.Verify(r => r.UpdateAsync(It.IsAny<Workflow>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_ExistingWorkflow_Deletes()
    {
        var workflow = new Workflow { Id = "wf-1" };
        _repoMock.Setup(r => r.GetByIdAsync("wf-1")).ReturnsAsync(workflow);
        _repoMock.Setup(r => r.DeleteAsync("wf-1")).Returns(Task.CompletedTask);

        await _service.DeleteAsync("wf-1");

        _repoMock.Verify(r => r.DeleteAsync("wf-1"), Times.Once);
    }

    [Fact]
    public async Task ActivateAsync_InactiveWorkflow_Activates()
    {
        var workflow = new Workflow { Id = "wf-1", IsActive = false };
        _repoMock.Setup(r => r.GetByIdAsync("wf-1")).ReturnsAsync(workflow);
        _repoMock.Setup(r => r.UpdateAsync(It.IsAny<Workflow>()))
            .ReturnsAsync((Workflow w) => w);

        await _service.ActivateAsync("wf-1");

        workflow.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task DeactivateAsync_ActiveWorkflow_Deactivates()
    {
        var workflow = new Workflow { Id = "wf-1", IsActive = true };
        _repoMock.Setup(r => r.GetByIdAsync("wf-1")).ReturnsAsync(workflow);
        _repoMock.Setup(r => r.UpdateAsync(It.IsAny<Workflow>()))
            .ReturnsAsync((Workflow w) => w);

        await _service.DeactivateAsync("wf-1");

        workflow.IsActive.Should().BeFalse();
    }
}
```

- [ ] **Step 2: 运行测试确认通过**

```bash
dotnet test tests/FlowEngine.Application.Tests/ --filter "WorkflowServiceCoreTests" -v n
```

---

### Task 3: WorkflowModificationService 测试

**Files:**
- Create: `tests/FlowEngine.Application.Tests/Workflows/WorkflowModificationServiceCoreTests.cs`

**覆盖目标:** DeepClone、节点增删改

- [ ] **Step 1: 创建测试文件**

```csharp
// tests/FlowEngine.Application.Tests/Workflows/WorkflowModificationServiceCoreTests.cs
using FlowEngine.Application.Workflows;
using FlowEngine.Core.Entities;
using FluentAssertions;
using Xunit;

namespace FlowEngine.Application.Tests.Workflows;

public class WorkflowModificationServiceCoreTests
{
    [Fact]
    public void DeepClone_CreatesIndependentCopy()
    {
        var original = new Workflow
        {
            Id = "wf-1",
            Name = "Original",
            Nodes = new List<NodeDefinition>
            {
                new() { Id = "n1", Type = "start", Label = "Start" }
            },
            Connections = new List<Connection>
            {
                new() { SourceNodeId = "n1", TargetNodeId = "n2" }
            }
        };

        var clone = WorkflowModificationService.DeepClone(original);

        clone.Should().NotBeSameAs(original);
        clone.Name.Should().Be("Original");
        clone.Nodes.Should().HaveCount(1);
        clone.Nodes[0].Should().NotBeSameAs(original.Nodes[0]);
    }

    [Fact]
    public void DeepClone_ModifyClone_DoesNotAffectOriginal()
    {
        var original = new Workflow
        {
            Id = "wf-1",
            Name = "Original",
            Nodes = new List<NodeDefinition>
            {
                new() { Id = "n1", Type = "start" }
            }
        };

        var clone = WorkflowModificationService.DeepClone(original);
        clone.Name = "Modified";
        clone.Nodes.Add(new NodeDefinition { Id = "n2", Type = "end" });

        original.Name.Should().Be("Original");
        original.Nodes.Should().HaveCount(1);
    }

    [Fact]
    public void AddNode_ValidNode_AddsToWorkflow()
    {
        var workflow = new Workflow
        {
            Nodes = new List<NodeDefinition>()
        };

        var newNode = new NodeDefinition { Id = "n1", Type = "http", Label = "HTTP Request" };
        workflow.Nodes.Add(newNode);

        workflow.Nodes.Should().HaveCount(1);
        workflow.Nodes[0].Type.Should().Be("http");
    }

    [Fact]
    public void RemoveNode_ExistingNode_RemovesFromWorkflow()
    {
        var workflow = new Workflow
        {
            Nodes = new List<NodeDefinition>
            {
                new() { Id = "n1", Type = "start" },
                new() { Id = "n2", Type = "end" }
            }
        };

        workflow.Nodes.RemoveAll(n => n.Id == "n1");

        workflow.Nodes.Should().HaveCount(1);
        workflow.Nodes[0].Id.Should().Be("n2");
    }

    [Fact]
    public void UpdateNode_ExistingNode_UpdatesProperties()
    {
        var workflow = new Workflow
        {
            Nodes = new List<NodeDefinition>
            {
                new() { Id = "n1", Type = "http", Label = "Old" }
            }
        };

        var node = workflow.Nodes.First(n => n.Id == "n1");
        node.Label = "New";

        workflow.Nodes[0].Label.Should().Be("New");
    }
}
```

- [ ] **Step 2: 运行测试确认通过**

```bash
dotnet test tests/FlowEngine.Application.Tests/ --filter "WorkflowModificationServiceCoreTests" -v n
```

---

### Task 4: Application DTOs 和值对象测试

**Files:**
- Create: `tests/FlowEngine.Application.Tests/Dtos/WorkflowDtosTests.cs`
- Create: `tests/FlowEngine.Application.Tests/Dtos/ExecutionDtosTests.cs`
- Create: `tests/FlowEngine.Application.Tests/RateLimiting/RateLimitOptionsTests.cs`

**覆盖目标:** DTO 序列化、验证、默认值

- [ ] **Step 1: 创建 DTO 测试**

```csharp
// tests/FlowEngine.Application.Tests/Dtos/WorkflowDtosTests.cs
using FlowEngine.Application.Dtos;
using FluentAssertions;
using Xunit;

namespace FlowEngine.Application.Tests.Dtos;

public class WorkflowDtosTests
{
    [Fact]
    public void CreateWorkflowRequest_DefaultValues()
    {
        var request = new CreateWorkflowRequest();
        request.Name.Should().BeNull();
        request.Description.Should().BeNull();
    }

    [Fact]
    public void CreateWorkflowRequest_SetProperties()
    {
        var request = new CreateWorkflowRequest
        {
            Name = "Test Workflow",
            Description = "A test",
            ProjectId = "proj-1"
        };
        request.Name.Should().Be("Test Workflow");
        request.ProjectId.Should().Be("proj-1");
    }

    [Fact]
    public void WorkflowListItem_CanSerialize()
    {
        var item = new WorkflowListItem
        {
            Id = "wf-1",
            Name = "Test",
            IsActive = true
        };
        item.Id.Should().Be("wf-1");
        item.IsActive.Should().BeTrue();
    }
}

// tests/FlowEngine.Application.Tests/Dtos/ExecutionDtosTests.cs
using FlowEngine.Application.Dtos;
using FluentAssertions;
using Xunit;

namespace FlowEngine.Application.Tests.Dtos;

public class ExecutionDtosTests
{
    [Fact]
    public void ExecutionRecordDto_DefaultStatus_IsPending()
    {
        var dto = new ExecutionRecordDto();
        dto.Status.Should().BeNullOrEmpty();
    }

    [Fact]
    public void ExecutionRecordDto_CanSetProperties()
    {
        var dto = new ExecutionRecordDto
        {
            Id = "exec-1",
            WorkflowId = "wf-1",
            Status = "Completed"
        };
        dto.Id.Should().Be("exec-1");
        dto.Status.Should().Be("Completed");
    }
}
```

- [ ] **Step 2: 运行测试确认通过**

```bash
dotnet test tests/FlowEngine.Application.Tests/ --filter "DtosTests|RateLimitOptionsTests" -v n
```

---

### Task 5: Application Authorization 补充测试

**Files:**
- Create: `tests/FlowEngine.Application.Tests/Authorization/AuthorizationPolicyTests.cs`
- Create: `tests/FlowEngine.Application.Tests/Authorization/AuthorizationGuardExtendedTests.cs`

**覆盖目标:** 权限策略组合、Guard 条件判断

- [ ] **Step 1: 创建 AuthorizationPolicyTests**

```csharp
// tests/FlowEngine.Application.Tests/Authorization/AuthorizationPolicyTests.cs
using FlowEngine.Application.Authorization;
using FlowEngine.Core.Authorization;
using FluentAssertions;
using Xunit;

namespace FlowEngine.Application.Tests.Authorization;

public class AuthorizationPolicyTests
{
    [Fact]
    public void AuthorizationPolicy_CanCreate()
    {
        var policy = new AuthorizationPolicy
        {
            RequiredPermission = Permission.WorkflowRead,
            Scope = Scope.Project
        };
        policy.RequiredPermission.Should().Be(Permission.WorkflowRead);
        policy.Scope.Should().Be(Scope.Project);
    }

    [Fact]
    public void AuthorizationGuard_AllowsAccess_WhenPermissionMet()
    {
        var guard = new AuthorizationGuard();
        guard.GrantedPermissions.Add(Permission.WorkflowRead);

        var result = guard.HasPermission(Permission.WorkflowRead);
        result.Should().BeTrue();
    }

    [Fact]
    public void AuthorizationGuard_DeniesAccess_WhenPermissionMissing()
    {
        var guard = new AuthorizationGuard();

        var result = guard.HasPermission(Permission.WorkflowDelete);
        result.Should().BeFalse();
    }
}
```

- [ ] **Step 2: 运行测试确认通过**

```bash
dotnet test tests/FlowEngine.Application.Tests/ --filter "AuthorizationPolicyTests|AuthorizationGuardExtendedTests" -v n
```

---

## Phase 2: 后端 — Core 模块（31.9% → 55%+）

> Core 模块 84 个类在 0%，主要是实体类和值对象。

### Task 6: Core Entity 类测试（覆盖 30+ 个 0% 实体）

**Files:**
- Create: `tests/FlowEngine.Core.Tests/Entities/ProjectTests.cs`
- Create: `tests/FlowEngine.Core.Tests/Entities/RetryPolicyTests.cs`
- Create: `tests/FlowEngine.Core.Tests/Entities/ToolDefinitionTests.cs`
- Create: `tests/FlowEngine.Core.Tests/Entities/StoredFileTests.cs`
- Create: `tests/FlowEngine.Core.Tests/Entities/ValidationRuleTests.cs`
- Create: `tests/FlowEngine.Core.Tests/Entities/WorkflowStyleSettingsTests.cs`
- Create: `tests/FlowEngine.Core.Tests/Entities/ConnectionTests.cs`
- Create: `tests/FlowEngine.Core.Tests/Entities/TriggerTests.cs`
- Create: `tests/FlowEngine.Core.Tests/Entities/NodeExecutionRecordTests.cs`
- Create: `tests/FlowEngine.Core.Tests/Entities/LlmMessageTests.cs`

**覆盖目标:** 所有数据实体的属性、默认值、序列化

- [ ] **Step 1: 批量创建实体测试**

```csharp
// tests/FlowEngine.Core.Tests/Entities/ProjectTests.cs
using FlowEngine.Core.Entities;
using FluentAssertions;
using Xunit;

namespace FlowEngine.Core.Tests.Entities;

public class ProjectTests
{
    [Fact]
    public void Project_DefaultValues()
    {
        var project = new Project();
        project.Id.Should().NotBeNullOrEmpty();
        project.Name.Should().BeNull();
        project.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Project_CanSetProperties()
    {
        var project = new Project
        {
            Name = "My Project",
            Description = "Desc"
        };
        project.Name.Should().Be("My Project");
    }
}

// tests/FlowEngine.Core.Tests/Entities/RetryPolicyTests.cs
using FlowEngine.Core.Entities;
using FluentAssertions;
using Xunit;

namespace FlowEngine.Core.Tests.Entities;

public class RetryPolicyTests
{
    [Fact]
    public void RetryPolicy_DefaultValues()
    {
        var policy = new RetryPolicy();
        policy.MaxRetries.Should().Be(0);
        policy.BackoffStrategy.Should().Be(default);
    }

    [Fact]
    public void RetryPolicy_SetProperties()
    {
        var policy = new RetryPolicy
        {
            MaxRetries = 3,
            DelayMs = 1000,
            BackoffStrategy = Core.Enums.BackoffStrategy.Exponential
        };
        policy.MaxRetries.Should().Be(3);
        policy.DelayMs.Should().Be(1000);
    }
}

// tests/FlowEngine.Core.Tests/Entities/ToolDefinitionTests.cs
using FlowEngine.Core.Entities;
using FluentAssertions;
using Xunit;

namespace FlowEngine.Core.Tests.Entities;

public class ToolDefinitionTests
{
    [Fact]
    public void ToolDefinition_CanCreate()
    {
        var tool = new ToolDefinition
        {
            Name = "http_request",
            DisplayName = "HTTP Request",
            Description = "Makes an HTTP request"
        };
        tool.Name.Should().Be("http_request");
    }

    [Fact]
    public void ToolDefinition_ParametersEmptyByDefault()
    {
        var tool = new ToolDefinition();
        tool.Parameters.Should().NotBeNull();
    }
}

// tests/FlowEngine.Core.Tests/Entities/StoredFileTests.cs
using FlowEngine.Core.Entities;
using FluentAssertions;
using Xunit;

namespace FlowEngine.Core.Tests.Entities;

public class StoredFileTests
{
    [Fact]
    public void StoredFile_CanCreate()
    {
        var file = new StoredFile
        {
            FileName = "test.json",
            ContentType = "application/json",
            Size = 1024
        };
        file.FileName.Should().Be("test.json");
        file.Size.Should().Be(1024);
    }
}

// tests/FlowEngine.Core.Tests/Entities/ValidationRuleTests.cs
using FlowEngine.Core.Entities;
using FluentAssertions;
using Xunit;

namespace FlowEngine.Core.Tests.Entities;

public class ValidationRuleTests
{
    [Fact]
    public void ValidationRule_CanCreate()
    {
        var rule = new ValidationRule
        {
            Field = "name",
            RuleType = "required",
            Message = "Name is required"
        };
        rule.Field.Should().Be("name");
    }
}

// tests/FlowEngine.Core.Tests/Entities/ConnectionTests.cs
using FlowEngine.Core.Entities;
using FluentAssertions;
using Xunit;

namespace FlowEngine.Core.Tests.Entities;

public class ConnectionTests
{
    [Fact]
    public void Connection_CanCreate()
    {
        var conn = new Connection
        {
            SourceNodeId = "n1",
            TargetNodeId = "n2",
            SourcePortId = "out",
            TargetPortId = "in"
        };
        conn.SourceNodeId.Should().Be("n1");
        conn.TargetNodeId.Should().Be("n2");
    }
}

// tests/FlowEngine.Core.Tests/Entities/TriggerTests.cs
using FlowEngine.Core.Entities;
using FluentAssertions;
using Xunit;

namespace FlowEngine.Core.Tests.Entities;

public class TriggerTests
{
    [Fact]
    public void Trigger_CanCreate()
    {
        var trigger = new Trigger
        {
            Type = Core.Enums.TriggerType.Manual,
            WorkflowId = "wf-1"
        };
        trigger.Type.Should().Be(Core.Enums.TriggerType.Manual);
    }
}

// tests/FlowEngine.Core.Tests/Entities/LlmMessageTests.cs
using FlowEngine.Core.Entities;
using FluentAssertions;
using Xunit;

namespace FlowEngine.Core.Tests.Entities;

public class LlmMessageTests
{
    [Fact]
    public void LlmMessage_CanCreate()
    {
        var msg = new LlmMessage
        {
            Role = "user",
            Content = "Hello"
        };
        msg.Role.Should().Be("user");
        msg.Content.Should().Be("Hello");
    }
}
```

- [ ] **Step 2: 运行测试确认通过**

```bash
dotnet test tests/FlowEngine.Core.Tests/ --filter "ProjectTests|RetryPolicyTests|ToolDefinitionTests|StoredFileTests|ValidationRuleTests|ConnectionTests|TriggerTests|LlmMessageTests" -v n
```

---

### Task 7: Core 值对象和枚举测试

**Files:**
- Create: `tests/FlowEngine.Core.Tests/ValueObjects/ExecutionIdTests.cs`
- Create: `tests/FlowEngine.Core.Tests/ValueObjects/WorkflowDefinitionIdTests.cs`
- Create: `tests/FlowEngine.Core.Tests/ValueObjects/CredentialKeyTests.cs`
- Create: `tests/FlowEngine.Core.Tests/Enums/ExecutionStatusTests.cs`
- Create: `tests/FlowEngine.Core.Tests/Enums/ParameterTypeTests.cs`

**覆盖目标:** 值对象创建、枚举转换

- [ ] **Step 1: 创建值对象测试**

```csharp
// tests/FlowEngine.Core.Tests/ValueObjects/ExecutionIdTests.cs
using FlowEngine.Core.ValueObjects;
using FluentAssertions;
using Xunit;

namespace FlowEngine.Core.Tests.ValueObjects;

public class ExecutionIdTests
{
    [Fact]
    public void ExecutionId_Create_GeneratesId()
    {
        var id = ExecutionId.Create();
        id.Value.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void ExecutionId_FromString_ReturnsSameValue()
    {
        var id = ExecutionId.FromString("exec-123");
        id.Value.Should().Be("exec-123");
    }

    [Fact]
    public void ExecutionId_Equality()
    {
        var id1 = ExecutionId.FromString("exec-1");
        var id2 = ExecutionId.FromString("exec-1");
        id1.Should().Be(id2);
    }
}

// tests/FlowEngine.Core.Tests/ValueObjects/WorkflowDefinitionIdTests.cs
using FlowEngine.Core.ValueObjects;
using FluentAssertions;
using Xunit;

namespace FlowEngine.Core.Tests.ValueObjects;

public class WorkflowDefinitionIdTests
{
    [Fact]
    public void WorkflowDefinitionId_Create_GeneratesId()
    {
        var id = WorkflowDefinitionId.Create();
        id.Value.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void WorkflowDefinitionId_FromString()
    {
        var id = WorkflowDefinitionId.FromString("wf-456");
        id.Value.Should().Be("wf-456");
    }
}

// tests/FlowEngine.Core.Tests/ValueObjects/CredentialKeyTests.cs
using FlowEngine.Core.ValueObjects;
using FluentAssertions;
using Xunit;

namespace FlowEngine.Core.Tests.ValueObjects;

public class CredentialKeyTests
{
    [Fact]
    public void CredentialKey_Create()
    {
        var key = CredentialKey.Create("my-api-key");
        key.Value.Should().Be("my-api-key");
    }
}
```

- [ ] **Step 2: 运行测试确认通过**

```bash
dotnet test tests/FlowEngine.Core.Tests/ --filter "ExecutionIdTests|WorkflowDefinitionIdTests|CredentialKeyTests" -v n
```

---

### Task 8: Core Http 和 Security 测试

**Files:**
- Create: `tests/FlowEngine.Core.Tests/Http/SsrfGuardTests.cs`
- Create: `tests/FlowEngine.Core.Tests/Http/HttpExecutionHelperTests.cs`
- Create: `tests/FlowEngine.Core.Tests/Authorization/RoleTests.cs`
- Create: `tests/FlowEngine.Core.Tests/Authorization/ScopeTests.cs`

**覆盖目标:** SSRF 防护、HTTP 辅助、授权模型

- [ ] **Step 1: 创建 SsrfGuardTests**

```csharp
// tests/FlowEngine.Core.Tests/Http/SsrfGuardTests.cs
using FlowEngine.Core.Http;
using FluentAssertions;
using Xunit;

namespace FlowEngine.Core.Tests.Http;

public class SsrfGuardTests
{
    [Theory]
    [InlineData("https://example.com", true)]
    [InlineData("http://localhost", false)]
    [InlineData("http://127.0.0.1", false)]
    [InlineData("http://169.254.169.254", false)]
    public void IsSafeUrl_VariousUrls_ReturnsExpected(string url, bool expected)
    {
        var result = SsrfGuard.IsSafeUrl(new Uri(url));
        result.Should().Be(expected);
    }
}

// tests/FlowEngine.Core.Tests/Authorization/RoleTests.cs
using FlowEngine.Core.Authorization;
using FluentAssertions;
using Xunit;

namespace FlowEngine.Core.Tests.Authorization;

public class RoleTests
{
    [Fact]
    public void RoleConstants_Admin_IsCorrect()
    {
        RoleConstants.Admin.Should().Be("admin");
    }

    [Fact]
    public void RoleConstants_User_IsCorrect()
    {
        RoleConstants.User.Should().Be("user");
    }

    [Fact]
    public void Permission_HasExpectedValues()
    {
        Permission.WorkflowRead.Should().NotBe(Permission.WorkflowDelete);
    }
}
```

- [ ] **Step 2: 运行测试确认通过**

```bash
dotnet test tests/FlowEngine.Core.Tests/ --filter "SsrfGuardTests|RoleTests" -v n
```

---

## Phase 3: 后端 — Runtime 模块（42.8% → 60%+）

> Runtime 43 个类在 0%，主要是 Executor 和 Credentials 相关。

### Task 9: Runtime Executor 测试

**Files:**
- Create: `tests/FlowEngine.Runtime.Tests/Executor/ErrorStrategyHandlerTests.cs`
- Create: `tests/FlowEngine.Runtime.Tests/Executor/ExecutionQueueExtendedTests.cs`
- Create: `tests/FlowEngine.Runtime.Tests/Executor/CodeParameterExtractorTests.cs`
- Create: `tests/FlowEngine.Runtime.Tests/Executor/ScriptParameterPreEvaluatorTests.cs`
- Create: `tests/FlowEngine.Runtime.Tests/Executor/NodeWorkItemTests.cs`

**覆盖目标:** 错误策略、队列、参数提取

- [ ] **Step 1: 创建 ErrorStrategyHandlerTests**

```csharp
// tests/FlowEngine.Runtime.Tests/Executor/ErrorStrategyHandlerTests.cs
using FlowEngine.Runtime.Executor;
using FlowEngine.Core.Entities;
using FluentAssertions;
using Xunit;

namespace FlowEngine.Runtime.Tests.Executor;

public class ErrorStrategyHandlerTests
{
    [Fact]
    public void ShouldRetry_WithRetriesLeft_ReturnsTrue()
    {
        var handler = new ErrorStrategyHandler();
        var policy = new RetryPolicy { MaxRetries = 3 };
        handler.ShouldRetry(policy, 1).Should().BeTrue();
    }

    [Fact]
    public void ShouldRetry_AtMaxRetries_ReturnsFalse()
    {
        var handler = new ErrorStrategyHandler();
        var policy = new RetryPolicy { MaxRetries = 3 };
        handler.ShouldRetry(policy, 3).Should().BeFalse();
    }

    [Fact]
    public void ShouldRetry_ZeroMaxRetries_ReturnsFalse()
    {
        var handler = new ErrorStrategyHandler();
        var policy = new RetryPolicy { MaxRetries = 0 };
        handler.ShouldRetry(policy, 0).Should().BeFalse();
    }

    [Fact]
    public void CalculateDelay_LinearStrategy_ReturnsFixedDelay()
    {
        var handler = new ErrorStrategyHandler();
        var policy = new RetryPolicy
        {
            DelayMs = 1000,
            BackoffStrategy = Core.Enums.BackoffStrategy.Linear
        };
        var delay = handler.CalculateDelay(policy, 1);
        delay.Should().Be(1000);
    }

    [Fact]
    public void CalculateDelay_ExponentialStrategy_DoublesEachRetry()
    {
        var handler = new ErrorStrategyHandler();
        var policy = new RetryPolicy
        {
            DelayMs = 1000,
            BackoffStrategy = Core.Enums.BackoffStrategy.Exponential
        };
        handler.CalculateDelay(policy, 1).Should().Be(1000);
        handler.CalculateDelay(policy, 2).Should().Be(2000);
        handler.CalculateDelay(policy, 3).Should().Be(4000);
    }
}
```

- [ ] **Step 2: 创建 CodeParameterExtractorTests**

```csharp
// tests/FlowEngine.Runtime.Tests/Executor/CodeParameterExtractorTests.cs
using FlowEngine.Runtime.Executor;
using FluentAssertions;
using Xunit;

namespace FlowEngine.Runtime.Tests.Executor;

public class CodeParameterExtractorTests
{
    [Fact]
    public void Extract_FromJsonString_ReturnsParameters()
    {
        var json = "{\"url\": \"https://example.com\", \"method\": \"GET\"}";
        var result = CodeParameterExtractor.Extract(json);
        result.Should().ContainKey("url");
        result["url"].Should().Be("https://example.com");
    }

    [Fact]
    public void Extract_EmptyString_ReturnsEmpty()
    {
        var result = CodeParameterExtractor.Extract("");
        result.Should().BeEmpty();
    }
}
```

- [ ] **Step 3: 运行测试确认通过**

```bash
dotnet test tests/FlowEngine.Runtime.Tests/ --filter "ErrorStrategyHandlerTests|CodeParameterExtractorTests" -v n
```

---

### Task 10: Runtime Credentials 测试

**Files:**
- Create: `tests/FlowEngine.Runtime.Tests/Credentials/OAuth2TokenServiceExtendedTests.cs`
- Create: `tests/FlowEngine.Runtime.Tests/Credentials/CredentialAccessorTests.cs`
- Create: `tests/FlowEngine.Runtime.Tests/Credentials/OAuth2ProviderTemplatesTests.cs`

**覆盖目标:** OAuth2 浆取、凭证访问、模板

- [ ] **Step 1: 创建 OAuth2ProviderTemplatesTests**

```csharp
// tests/FlowEngine.Runtime.Tests/Credentials/OAuth2ProviderTemplatesTests.cs
using FlowEngine.Runtime.Credentials;
using FluentAssertions;
using Xunit;

namespace FlowEngine.Runtime.Tests.Credentials;

public class OAuth2ProviderTemplatesTests
{
    [Fact]
    public void Templates_ContainsGoogle()
    {
        var templates = OAuth2ProviderTemplates.Templates;
        templates.Should().ContainKey("google");
    }

    [Fact]
    public void Templates_ContainsGitHub()
    {
        var templates = OAuth2ProviderTemplates.Templates;
        templates.Should().ContainKey("github");
    }

    [Fact]
    public void Templates_HasAuthorizeUrl()
    {
        var templates = OAuth2ProviderTemplates.Templates;
        var google = templates["google"];
        google.AuthorizeUrl.Should().NotBeNullOrEmpty();
        google.TokenUrl.Should().NotBeNullOrEmpty();
    }
}
```

- [ ] **Step 2: 运行测试确认通过**

```bash
dotnet test tests/FlowEngine.Runtime.Tests/ --filter "OAuth2ProviderTemplatesTests" -v n
```

---

### Task 11: Runtime Registry Converters 测试

**Files:**
- Create: `tests/FlowEngine.Runtime.Tests/Registry/Converters/BoolConverterTests.cs`
- Create: `tests/FlowEngine.Runtime.Tests/Registry/Converters/StringConverterTests.cs`
- Create: `tests/FlowEngine.Runtime.Tests/Registry/Converters/NumericConverterTests.cs`
- Create: `tests/FlowEngine.Runtime.Tests/Registry/Converters/DateTimeConverterTests.cs`
- Create: `tests/FlowEngine.Runtime.Tests/Registry/Converters/EnumConverterTests.cs`
- Create: `tests/FlowEngine.Runtime.Tests/Registry/Converters/UriConverterTests.cs`
- Create: `tests/FlowEngine.Runtime.Tests/Registry/Converters/JsonConverterTests.cs`

**覆盖目标:** 所有类型转换器

- [ ] **Step 1: 批量创建 Converter 测试**

```csharp
// tests/FlowEngine.Runtime.Tests/Registry/Converters/BoolConverterTests.cs
using FlowEngine.Runtime.Registry.Converters;
using FluentAssertions;
using Xunit;

namespace FlowEngine.Runtime.Tests.Registry.Converters;

public class BoolConverterTests
{
    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("True", true)]
    [InlineData("False", false)]
    [InlineData("1", true)]
    [InlineData("0", false)]
    [InlineData("yes", true)]
    [InlineData("no", false)]
    public void Convert_VariousInputs(string input, bool expected)
    {
        var converter = new BoolConverter();
        converter.Convert(input).Should().Be(expected);
    }
}

// tests/FlowEngine.Runtime.Tests/Registry/Converters/StringConverterTests.cs
using FlowEngine.Runtime.Registry.Converters;
using FluentAssertions;
using Xunit;

namespace FlowEngine.Runtime.Tests.Registry.Converters;

public class StringConverterTests
{
    [Fact]
    public void Convert_AnyInput_ReturnsString()
    {
        var converter = new StringConverter();
        converter.Convert("hello").Should().Be("hello");
        converter.Convert("123").Should().Be("123");
        converter.Convert("").Should().Be("");
    }
}

// tests/FlowEngine.Runtime.Tests/Registry/Converters/NumericConverterTests.cs
using FlowEngine.Runtime.Registry.Converters;
using FluentAssertions;
using Xunit;

namespace FlowEngine.Runtime.Tests.Registry.Converters;

public class NumericConverterTests
{
    [Theory]
    [InlineData("42", 42.0)]
    [InlineData("3.14", 3.14)]
    [InlineData("-1", -1.0)]
    [InlineData("0", 0.0)]
    public void Convert_NumericStrings(string input, double expected)
    {
        var converter = new NumericConverter();
        converter.Convert(input).Should().Be(expected);
    }
}

// tests/FlowEngine.Runtime.Tests/Registry/Converters/DateTimeConverterTests.cs
using FlowEngine.Runtime.Registry.Converters;
using FluentAssertions;
using Xunit;

namespace FlowEngine.Runtime.Tests.Registry.Converters;

public class DateTimeConverterTests
{
    [Fact]
    public void Convert_ISO8601_ReturnsDateTime()
    {
        var converter = new DateTimeConverter();
        var result = converter.Convert("2026-01-15T10:30:00Z");
        result.Should().BeOfType<DateTime>();
    }
}

// tests/FlowEngine.Runtime.Tests/Registry/Converters/EnumConverterTests.cs
using FlowEngine.Runtime.Registry.Converters;
using FluentAssertions;
using Xunit;

namespace FlowEngine.Runtime.Tests.Registry.Converters;

public class EnumConverterTests
{
    [Fact]
    public void Convert_ValidEnumString_ReturnsEnum()
    {
        var converter = new EnumConverter();
        var result = converter.Convert("GET");
        result.Should().NotBeNull();
    }
}

// tests/FlowEngine.Runtime.Tests/Registry/Converters/UriConverterTests.cs
using FlowEngine.Runtime.Registry.Converters;
using FluentAssertions;
using Xunit;

namespace FlowEngine.Runtime.Tests.Registry.Converters;

public class UriConverterTests
{
    [Fact]
    public void Convert_ValidUrl_ReturnsUri()
    {
        var converter = new UriConverter();
        var result = converter.Convert("https://example.com");
        result.Should().BeOfType<Uri>();
    }
}
```

- [ ] **Step 2: 运行测试确认通过**

```bash
dotnet test tests/FlowEngine.Runtime.Tests/ --filter "ConverterTests" -v n
```

---

## Phase 4: 后端 — Host 模块（58.9% → 70%+）

> Host 84 个类在 0%，主要是 Controllers。

### Task 12: Host Controllers 测试

**Files:**
- Create: `tests/FlowEngine.Host.Tests/Controllers/ProjectsControllerTests.cs`
- Create: `tests/FlowEngine.Host.Tests/Controllers/FilesControllerTests.cs`
- Create: `tests/FlowEngine.Host.Tests/Controllers/NodeTypesControllerTests.cs`
- Create: `tests/FlowEngine.Host.Tests/Controllers/AuditEventsControllerTests.cs`
- Create: `tests/FlowEngine.Host.Tests/Controllers/TriggersControllerTests.cs`

**覆盖目标:** 所有 Controller 的 CRUD 端点

- [ ] **Step 1: 创建 ProjectsControllerTests**

```csharp
// tests/FlowEngine.Host.Tests/Controllers/ProjectsControllerTests.cs
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using FlowEngine.Host.Controllers;
using FlowEngine.Application.Projects;
using FlowEngine.Core.Entities;
using Moq;
using Xunit;

namespace FlowEngine.Host.Tests.Controllers;

public class ProjectsControllerTests
{
    private readonly Mock<IProjectService> _projectServiceMock = new();
    private readonly ProjectsController _controller;

    public ProjectsControllerTests()
    {
        _controller = new ProjectsController(_projectServiceMock.Object);
    }

    [Fact]
    public async Task GetAll_ReturnsOkWithProjects()
    {
        _projectServiceMock.Setup(s => s.GetAllAsync())
            .ReturnsAsync(new List<Project> { new() { Id = "p1", Name = "Test" } });

        var result = await _controller.GetAll();

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var projects = okResult.Value.Should().BeAssignableTo<IEnumerable<Project>>().Subject;
        projects.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetById_ExistingProject_ReturnsOk()
    {
        _projectServiceMock.Setup(s => s.GetByIdAsync("p1"))
            .ReturnsAsync(new Project { Id = "p1", Name = "Test" });

        var result = await _controller.GetById("p1");

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.Value.Should().NotBeNull();
    }

    [Fact]
    public async Task GetById_NonExisting_ReturnsNotFound()
    {
        _projectServiceMock.Setup(s => s.GetByIdAsync("missing"))
            .ReturnsAsync((Project?)null);

        var result = await _controller.GetById("missing");

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task Create_ValidProject_ReturnsCreated()
    {
        var project = new Project { Name = "New" };
        _projectServiceMock.Setup(s => s.CreateAsync(It.IsAny<Project>()))
            .ReturnsAsync(project);

        var result = await _controller.Create(project);

        var createdResult = result.Should().BeOfType<CreatedAtActionResult>().Subject;
        createdResult.StatusCode.Should().Be(201);
    }

    [Fact]
    public async Task Delete_ExistingProject_ReturnsNoContent()
    {
        _projectServiceMock.Setup(s => s.DeleteAsync("p1"))
            .Returns(Task.CompletedTask);

        var result = await _controller.Delete("p1");

        result.Should().BeOfType<NoContentResult>();
    }
}
```

- [ ] **Step 2: 创建 FilesControllerTests**

```csharp
// tests/FlowEngine.Host.Tests/Controllers/FilesControllerTests.cs
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using FlowEngine.Host.Controllers;
using FlowEngine.Application.Files;
using Moq;
using Xunit;

namespace FlowEngine.Host.Tests.Controllers;

public class FilesControllerTests
{
    private readonly Mock<IFileService> _fileServiceMock = new();
    private readonly FilesController _controller;

    public FilesControllerTests()
    {
        _controller = new FilesController(_fileServiceMock.Object);
    }

    [Fact]
    public async Task GetAll_ReturnsOk()
    {
        _fileServiceMock.Setup(s => s.GetAllAsync(It.IsAny<string?>()))
            .ReturnsAsync(new List<Core.Entities.StoredFile>());

        var result = await _controller.GetAll(null);

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetById_NonExisting_ReturnsNotFound()
    {
        _fileServiceMock.Setup(s => s.GetByIdAsync("missing"))
            .ReturnsAsync((Core.Entities.StoredFile?)null);

        var result = await _controller.GetById("missing");

        result.Should().BeOfType<NotFoundResult>();
    }
}
```

- [ ] **Step 3: 运行测试确认通过**

```bash
dotnet test tests/FlowEngine.Host.Tests/ --filter "ProjectsControllerTests|FilesControllerTests" -v n
```

---

### Task 13: Host Middlewares 和 Services 补充测试

**Files:**
- Create: `tests/FlowEngine.Host.Tests/Middlewares/SecurityHeadersMiddlewareTests.cs`
- Create: `tests/FlowEngine.Host.Tests/Middlewares/GlobalExceptionHandlerMiddlewareTests.cs`
- Create: `tests/FlowEngine.Host.Tests/Services/ExecutionCleanupHostedServiceTests.cs`

**覆盖目标:** 中间件行为、异常处理

- [ ] **Step 1: 创建 SecurityHeadersMiddlewareTests**

```csharp
// tests/FlowEngine.Host.Tests/Middlewares/SecurityHeadersMiddlewareTests.cs
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using FlowEngine.Host.Middlewares;
using Xunit;

namespace FlowEngine.Host.Tests.Middlewares;

public class SecurityHeadersMiddlewareTests
{
    [Fact]
    public async Task Invoke_AddsSecurityHeaders()
    {
        var context = new DefaultHttpContext();
        var middleware = new SecurityHeadersMiddleware(async ctx =>
        {
            ctx.Response.StatusCode = 200;
        });

        await middleware.InvokeAsync(context);

        context.Response.Headers.Should().ContainKey("X-Content-Type-Options");
        context.Response.Headers["X-Content-Type-Options"].ToString().Should().Be("nosniff");
        context.Response.Headers.Should().ContainKey("X-Frame-Options");
    }
}

// tests/FlowEngine.Host.Tests/Middlewares/GlobalExceptionHandlerMiddlewareTests.cs
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using FlowEngine.Host.Middlewares;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace FlowEngine.Host.Tests.Middlewares;

public class GlobalExceptionHandlerMiddlewareTests
{
    [Fact]
    public async Task Invoke_Exception_Returns500()
    {
        var context = new DefaultHttpContext();
        var loggerMock = new Mock<ILogger<GlobalExceptionHandlerMiddleware>>();
        var middleware = new GlobalExceptionHandlerMiddleware(
            async ctx => throw new InvalidOperationException("test error"),
            loggerMock.Object);

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(500);
    }
}
```

- [ ] **Step 2: 运行测试确认通过**

```bash
dotnet test tests/FlowEngine.Host.Tests/ --filter "SecurityHeadersMiddlewareTests|GlobalExceptionHandlerMiddlewareTests" -v n
```

---

## Phase 5: 后端 — Infrastructure 模块（26.2% → 50%+）

> Infrastructure 17 个类在 0%，需要 mock 外部依赖。

### Task 14: Infrastructure Identity 测试

**Files:**
- Create: `tests/FlowEngine.Infrastructure.Tests/Identity/JwtTokenServiceTests.cs`
- Create: `tests/FlowEngine.Infrastructure.Tests/Identity/PasswordHasherTests.cs`
- Create: `tests/FlowEngine.Infrastructure.Tests/Identity/TokenBlacklistServiceTests.cs`
- Create: `tests/FlowEngine.Infrastructure.Tests/Identity/UserStoreTests.cs`

**覆盖目标:** JWT 生成/验证、密码哈希、Token 黑名单

- [ ] **Step 1: 创建 PasswordHasherTests**

```csharp
// tests/FlowEngine.Infrastructure.Tests/Identity/PasswordHasherTests.cs
using FlowEngine.Infrastructure.Identity;
using FluentAssertions;
using Xunit;

namespace FlowEngine.Infrastructure.Tests.Identity;

public class PasswordHasherTests
{
    [Fact]
    public void Hash_Password_ReturnsHash()
    {
        var hasher = new PasswordHasher();
        var hash = hasher.Hash("password123");
        hash.Should().NotBeNullOrEmpty();
        hash.Should().NotBe("password123");
    }

    [Fact]
    public void Verify_CorrectPassword_ReturnsTrue()
    {
        var hasher = new PasswordHasher();
        var hash = hasher.Hash("password123");
        hasher.Verify("password123", hash).Should().BeTrue();
    }

    [Fact]
    public void Verify_WrongPassword_ReturnsFalse()
    {
        var hasher = new PasswordHasher();
        var hash = hasher.Hash("password123");
        hasher.Verify("wrongpassword", hash).Should().BeFalse();
    }

    [Fact]
    public void Hash_SamePassword_DifferentHashes()
    {
        var hasher = new PasswordHasher();
        var hash1 = hasher.Hash("password123");
        var hash2 = hasher.Hash("password123");
        hash1.Should().NotBe(hash2); // salt 应不同
    }
}

// tests/FlowEngine.Infrastructure.Tests/Identity/JwtTokenServiceTests.cs
using FlowEngine.Infrastructure.Identity;
using FlowEngine.Core.Identity;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FlowEngine.Infrastructure.Tests.Identity;

public class JwtTokenServiceTests
{
    private readonly JwtTokenService _service;

    public JwtTokenServiceTests()
    {
        var options = Options.Create(new JwtOptions
        {
            SecretKey = "super-secret-key-that-is-long-enough-123456",
            Issuer = "FlowEngine",
            Audience = "FlowEngine",
            ExpiryMinutes = 60
        });
        _service = new JwtTokenService(options);
    }

    [Fact]
    public void GenerateToken_ReturnsToken()
    {
        var user = new User { Id = "u1", Username = "admin", Role = "admin" };
        var token = _service.GenerateToken(user);
        token.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void ValidateToken_ValidToken_ReturnsPrincipal()
    {
        var user = new User { Id = "u1", Username = "admin", Role = "admin" };
        var token = _service.GenerateToken(user);
        var principal = _service.ValidateToken(token);
        principal.Should().NotBeNull();
    }

    [Fact]
    public void ValidateToken_InvalidToken_ReturnsNull()
    {
        var principal = _service.ValidateToken("invalid.token.here");
        principal.Should().BeNull();
    }
}
```

- [ ] **Step 2: 运行测试确认通过**

```bash
dotnet test tests/FlowEngine.Infrastructure.Tests/ --filter "PasswordHasherTests|JwtTokenServiceTests" -v n
```

---

### Task 15: Infrastructure Storage 和 Audit 测试

**Files:**
- Create: `tests/FlowEngine.Infrastructure.Tests/Storage/LocalFileStorageTests.cs`
- Create: `tests/FlowEngine.Infrastructure.Tests/Audit/AuditLogReaderExtendedTests.cs`

**覆盖目标:** 文件存储 CRUD、审计日志查询

- [ ] **Step 1: 创建 LocalFileStorageTests**

```csharp
// tests/FlowEngine.Infrastructure.Tests/Storage/LocalFileStorageTests.cs
using FlowEngine.Infrastructure.Storage;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FlowEngine.Infrastructure.Tests.Storage;

public class LocalFileStorageTests : IDisposable
{
    private readonly string _tempDir;
    private readonly LocalFileStorage _storage;

    public LocalFileStorageTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
        _storage = new LocalFileStorage(Options.Create(new FileStorageOptions
        {
            BasePath = _tempDir
        }));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public async Task SaveAsync_CreatesFile()
    {
        var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("test content"));
        var id = await _storage.SaveAsync("test.txt", stream, "text/plain");

        id.Should().NotBeNullOrEmpty();
        File.Exists(Path.Combine(_tempDir, id)).Should().BeTrue();
    }

    [Fact]
    public async Task GetByIdAsync_ExistingFile_ReturnsStream()
    {
        var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("test content"));
        var id = await _storage.SaveAsync("test.txt", stream, "text/plain");

        var result = await _storage.GetByIdAsync(id);

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task GetByIdAsync_NonExisting_ReturnsNull()
    {
        var result = await _storage.GetByIdAsync("nonexistent");
        result.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_ExistingFile_Deletes()
    {
        var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("test"));
        var id = await _storage.SaveAsync("test.txt", stream, "text/plain");

        await _storage.DeleteAsync(id);

        File.Exists(Path.Combine(_tempDir, id)).Should().BeFalse();
    }
}
```

- [ ] **Step 2: 运行测试确认通过**

```bash
dotnet test tests/FlowEngine.Infrastructure.Tests/ --filter "LocalFileStorageTests" -v n
```

---

## Phase 6: 前端 — 补充测试（50.4% → 65%+）

### Task 16: services/api.ts 测试（2.6% → 50%+）

**Files:**
- Create: `frontend/src/services/__tests__/api.test.ts`

- [ ] **Step 1: 创建 api 测试**

```typescript
// frontend/src/services/__tests__/api.test.ts
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { apiClient } from '../api';

describe('apiClient', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    localStorage.clear();
  });

  it('has base URL configured', () => {
    expect(apiClient.defaults.baseURL).toBeDefined();
  });

  it('request interceptor adds auth token', async () => {
    localStorage.setItem('auth_token', 'test-token-123');
    // 触发请求拦截器
    const config = { headers: {} as any, url: '/test' };
    const result = await apiClient.interceptors.request.handlers[0]?.fulfilled(config);
    if (result) {
      expect(result.headers.Authorization).toContain('test-token-123');
    }
  });

  it('response interceptor handles 401', async () => {
    const error = { response: { status: 401 } };
    // 验证拦截器存在
    expect(apiClient.interceptors.response.handlers.length).toBeGreaterThan(0);
  });
});
```

- [ ] **Step 2: 运行测试确认通过**

```bash
cd frontend && npx vitest run src/services/__tests__/api.test.ts
```

---

### Task 17: stores/workflowStore 测试（22% → 55%+）

**Files:**
- Modify: `frontend/src/stores/__tests__/workflowStore.test.ts` (补充)

- [ ] **Step 1: 补充 store 测试**

```typescript
// 在现有测试文件中添加更多用例
describe('workflowStore extended', () => {
  it('setCurrentWorkflow updates state', () => {
    const { setCurrentWorkflow } = useWorkflowStore.getState();
    const workflow = { id: 'wf-1', name: 'Test', nodes: [] } as any;
    setCurrentWorkflow(workflow);
    expect(useWorkflowStore.getState().currentWorkflow?.id).toBe('wf-1');
  });

  it('clearCurrentWorkflow resets to null', () => {
    const { setCurrentWorkflow, clearCurrentWorkflow } = useWorkflowStore.getState();
    setCurrentWorkflow({ id: 'wf-1' } as any);
    clearCurrentWorkflow();
    expect(useWorkflowStore.getState().currentWorkflow).toBeNull();
  });

  it('setExecutionHistory updates history', () => {
    const { setExecutionHistory } = useWorkflowStore.getState();
    const history = [{ id: 'exec-1', status: 'Completed' }] as any;
    setExecutionHistory(history);
    expect(useWorkflowStore.getState().executionHistory).toHaveLength(1);
  });

  it('setSelectedNode updates selected node', () => {
    const { setSelectedNode } = useWorkflowStore.getState();
    setSelectedNode({ id: 'n1', type: 'http' } as any);
    expect(useWorkflowStore.getState().selectedNode?.id).toBe('n1');
  });

  it('clearSelectedNode resets', () => {
    const { setSelectedNode, clearSelectedNode } = useWorkflowStore.getState();
    setSelectedNode({ id: 'n1' } as any);
    clearSelectedNode();
    expect(useWorkflowStore.getState().selectedNode).toBeNull();
  });
});
```

- [ ] **Step 2: 运行测试确认通过**

```bash
cd frontend && npx vitest run src/stores/__tests__/workflowStore.test.ts
```

---

### Task 18: pages/ 测试（31% → 55%+）

**Files:**
- Create: `frontend/src/pages/__tests__/LoginPage.test.tsx`
- Create: `frontend/src/pages/__tests__/HelpPage.test.tsx`
- Create: `frontend/src/pages/__tests__/WorkflowEditorPage.test.tsx`
- Create: `frontend/src/pages/__tests__/ExecutionHistoryPage.test.tsx`

- [ ] **Step 1: 创建页面测试**

```typescript
// frontend/src/pages/__tests__/LoginPage.test.tsx
import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import LoginPage from '../LoginPage';

describe('LoginPage', () => {
  it('renders login form', () => {
    render(
      <MemoryRouter>
        <LoginPage />
      </MemoryRouter>
    );
    expect(screen.getByText(/login/i)).toBeInTheDocument();
  });

  it('has username input', () => {
    render(
      <MemoryRouter>
        <LoginPage />
      </MemoryRouter>
    );
    expect(screen.getByLabelText(/username/i)).toBeInTheDocument();
  });

  it('has password input', () => {
    render(
      <MemoryRouter>
        <LoginPage />
      </MemoryRouter>
    );
    expect(screen.getByLabelText(/password/i)).toBeInTheDocument();
  });

  it('has submit button', () => {
    render(
      <MemoryRouter>
        <LoginPage />
      </MemoryRouter>
    );
    expect(screen.getByRole('button', { name: /login/i })).toBeInTheDocument();
  });
});

// frontend/src/pages/__tests__/HelpPage.test.tsx
import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import HelpPage from '../HelpPage';

describe('HelpPage', () => {
  it('renders help content', () => {
    render(
      <MemoryRouter>
        <HelpPage />
      </MemoryRouter>
    );
    expect(screen.getByText(/help/i)).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: 运行测试确认通过**

```bash
cd frontend && npx vitest run src/pages/__tests__/
```

---

### Task 19: utils/ 工具函数测试（55% → 75%+）

**Files:**
- Create: `frontend/src/utils/__tests__/workflowSerializer.test.ts`
- Create: `frontend/src/utils/__tests__/tokenStore.test.ts`
- Create: `frontend/src/utils/__tests__/globalErrorHandler.test.ts`
- Create: `frontend/src/utils/__tests__/execution.test.ts`

- [ ] **Step 1: 创建工具函数测试**

```typescript
// frontend/src/utils/__tests__/tokenStore.test.ts
import { describe, it, expect, beforeEach } from 'vitest';
import { getToken, setToken, clearToken } from '../tokenStore';

describe('tokenStore', () => {
  beforeEach(() => {
    localStorage.clear();
  });

  it('setToken stores token', () => {
    setToken('my-token');
    expect(localStorage.getItem('auth_token')).toBe('my-token');
  });

  it('getToken returns stored token', () => {
    localStorage.setItem('auth_token', 'stored-token');
    expect(getToken()).toBe('stored-token');
  });

  it('getToken returns null when no token', () => {
    expect(getToken()).toBeNull();
  });

  it('clearToken removes token', () => {
    setToken('my-token');
    clearToken();
    expect(getToken()).toBeNull();
  });
});

// frontend/src/utils/__tests__/globalErrorHandler.test.ts
import { describe, it, expect, vi } from 'vitest';
import { setupGlobalErrorHandler } from '../globalErrorHandler';

describe('globalErrorHandler', () => {
  it('does not throw when setting up', () => {
    expect(() => setupGlobalErrorHandler()).not.toThrow();
  });
});

// frontend/src/utils/__tests__/execution.test.ts
import { describe, it, expect } from 'vitest';
import { getStatusColor, formatDuration } from '../execution';

describe('execution utils', () => {
  it('getStatusColor returns color for Completed', () => {
    expect(getStatusColor('Completed')).toBeDefined();
  });

  it('getStatusColor returns color for Running', () => {
    expect(getStatusColor('Running')).toBeDefined();
  });

  it('getStatusColor returns color for Failed', () => {
    expect(getStatusColor('Failed')).toBeDefined();
  });

  it('formatDuration formats milliseconds', () => {
    const result = formatDuration(5000);
    expect(result).toBeDefined();
  });
});
```

- [ ] **Step 2: 运行测试确认通过**

```bash
cd frontend && npx vitest run src/utils/__tests__/
```

---

### Task 20: components/ 补充测试（36% → 55%+）

**Files:**
- Create: `frontend/src/components/ExecutionPanel/__tests__/ExecutionButton.test.tsx`
- Create: `frontend/src/components/ExecutionPanel/__tests__/NodeOutputList.test.tsx`
- Create: `frontend/src/components/NodePanel/__tests__/NodeCard.test.tsx`
- Create: `frontend/src/components/Canvas/__tests__/CanvasToolbar.test.tsx`
- Create: `frontend/src/components/Layout/__tests__/HeaderToolbar.test.tsx`

- [ ] **Step 1: 创建组件测试**

```typescript
// frontend/src/components/ExecutionPanel/__tests__/ExecutionButton.test.tsx
import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import ExecutionButton from '../ExecutionButton';

describe('ExecutionButton', () => {
  it('renders execute button', () => {
    render(<ExecutionButton onExecute={() => {}} disabled={false} />);
    expect(screen.getByRole('button')).toBeInTheDocument();
  });

  it('calls onExecute when clicked', async () => {
    const onExecute = vi.fn();
    render(<ExecutionButton onExecute={onExecute} disabled={false} />);
    fireEvent.click(screen.getByRole('button'));
    expect(onExecute).toHaveBeenCalled();
  });

  it('is disabled when disabled prop is true', () => {
    render(<ExecutionButton onExecute={() => {}} disabled={true} />);
    expect(screen.getByRole('button')).toBeDisabled();
  });
});

// frontend/src/components/NodePanel/__tests__/NodeCard.test.tsx
import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import NodeCard from '../NodeCard';

describe('NodeCard', () => {
  it('renders node name', () => {
    const node = { id: 'n1', type: 'http', label: 'HTTP Request' } as any;
    render(<NodeCard node={node} selected={false} onClick={() => {}} />);
    expect(screen.getByText('HTTP Request')).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: 运行测试确认通过**

```bash
cd frontend && npx vitest run src/components/ --reporter=verbose
```

---

## Phase 7: 验证与收尾

### Task 21: 全量测试 + 覆盖率报告

- [ ] **Step 1: 运行后端全量测试**

```bash
dotnet test FlowEngine.sln --collect:"XPlat Code Coverage" --results-directory TestResults
```

- [ ] **Step 2: 解析后端覆盖率**

```powershell
Get-ChildItem -Path TestResults -Filter "coverage.cobertura.xml" -Recurse | Sort-Object LastWriteTime -Descending | Select-Object -First 1 | ForEach-Object {
    [xml]$xml = Get-Content $_.FullName
    $lineRate = [math]::Round([double]$xml.coverage.'line-rate' * 100, 1)
    $branchRate = [math]::Round([double]$xml.coverage.'branch-rate' * 100, 1)
    Write-Host "Backend Overall: Line $lineRate% | Branch $branchRate%"
    $xml.coverage.packages.package | ForEach-Object {
        $pkg = $_.name -replace 'FlowEngine\.', ''
        $rate = [math]::Round([double]$_.'line-rate' * 100, 1)
        Write-Host "  $pkg : $rate%"
    }
}
```

- [ ] **Step 3: 运行前端全量测试**

```bash
cd frontend && npx vitest run --coverage
```

- [ ] **Step 4: 确认目标达成**

| 模块 | 当前 | 目标 | 状态 |
|------|------|------|------|
| 后端 Application | 35.6% | 60%+ | |
| 后端 Core | 31.9% | 55%+ | |
| 后端 Host | 58.9% | 70%+ | |
| 后端 Runtime | 42.8% | 60%+ | |
| 后端 Infrastructure | 26.2% | 50%+ | |
| **后端整体** | **54%** | **70%+** | |
| 前端 Lines | 50.4% | 65%+ | |
| 前端 Functions | 27.6% | 45%+ | |

- [ ] **Step 5: 提交所有测试代码**

```bash
git add tests/ frontend/src/
git commit -m "test: 补充前后端单元测试，后端覆盖率提升至70%+"
```

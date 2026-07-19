using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;

namespace FlowEngine.Core.Tests;

/// <summary>
/// 实体属性往返测试（补充）：覆盖 Task 002 未涉及的其余 Core 实体。
/// </summary>
public class EntitiesMorePropertyTests
{
    [Fact]
    public void AgentExecutionConfig_Properties_RoundTrip()
    {
        var cfg = new AgentExecutionConfig
        {
            MaxIterations = 5,
            MaxNestingDepth = 2,
            MemoryEnabled = true,
            MemoryWindowSize = 50
        };

        Assert.Equal(5, cfg.MaxIterations);
        Assert.Equal(2, cfg.MaxNestingDepth);
        Assert.True(cfg.MemoryEnabled);
        Assert.Equal(50, cfg.MemoryWindowSize);
    }

    [Fact]
    public void AgentExecutionConfig_Defaults_AreExpected()
    {
        var cfg = new AgentExecutionConfig();

        Assert.Equal(10, cfg.MaxIterations);
        Assert.Equal(3, cfg.MaxNestingDepth);
        Assert.False(cfg.MemoryEnabled);
        Assert.Equal(20, cfg.MemoryWindowSize);
    }

    [Fact]
    public void Connection_Properties_RoundTrip()
    {
        var conn = new Connection
        {
            Id = Guid.CreateVersion7(),
            SourceNodeId = "a",
            SourcePortName = "out",
            TargetNodeId = "b",
            TargetPortName = "in",
            Condition = "x > 0"
        };

        Assert.Equal("a", conn.SourceNodeId);
        Assert.Equal("out", conn.SourcePortName);
        Assert.Equal("b", conn.TargetNodeId);
        Assert.Equal("in", conn.TargetPortName);
        Assert.Equal("x > 0", conn.Condition);
    }

    [Fact]
    public void Credential_Properties_RoundTrip()
    {
        var projectId = Guid.NewGuid();
        var credential = new Credential
        {
            Id = Guid.CreateVersion7(),
            ProjectId = projectId,
            Name = "api",
            Type = "apiKey",
            Data = new Dictionary<string, EncryptedField>
            {
                ["key"] = new() { CipherText = "ct", Nonce = "n", Tag = "t" }
            },
            KeyVersion = "v1"
        };

        Assert.Equal(projectId, credential.ProjectId);
        Assert.Equal("api", credential.Name);
        Assert.Equal("apiKey", credential.Type);
        Assert.Single(credential.Data);
        Assert.Equal("v1", credential.KeyVersion);
    }

    [Fact]
    public void CredentialValue_Properties_RoundTrip()
    {
        var value = new CredentialValue
        {
            Name = "api",
            Type = "apiKey",
            Fields = new Dictionary<string, string> { ["key"] = "secret" },
            BinaryFields = new Dictionary<string, byte[]> { ["cert"] = [1, 2, 3] }
        };

        Assert.Equal("api", value.Name);
        Assert.Equal("apiKey", value.Type);
        Assert.Equal("secret", value.Fields["key"]);
        Assert.Equal([1, 2, 3], value.BinaryFields["cert"]);
    }

    [Fact]
    public void DataSchema_Properties_RoundTrip()
    {
        var schema = new DataSchema
        {
            Type = "object",
            Properties = new Dictionary<string, DataSchema>
            {
                ["name"] = new() { Type = "string" }
            },
            Required = ["name"],
            Items = new DataSchema { Type = "string" },
            Description = "desc"
        };

        Assert.Equal("object", schema.Type);
        Assert.Single(schema.Properties);
        Assert.Single(schema.Required);
        Assert.NotNull(schema.Items);
        Assert.Equal("desc", schema.Description);
    }

    [Fact]
    public void DisplayRule_Properties_RoundTrip()
    {
        var rule = new DisplayRule
        {
            Condition = "x == 1",
            Dependencies = ["x", "y"]
        };

        Assert.Equal("x == 1", rule.Condition);
        Assert.Equal(2, rule.Dependencies.Count);
    }

    [Fact]
    public void EncryptedField_Properties_RoundTrip()
    {
        var field = new EncryptedField
        {
            CipherText = "ct",
            Nonce = "n",
            Tag = "t",
            IsBinary = true
        };

        Assert.Equal("ct", field.CipherText);
        Assert.Equal("n", field.Nonce);
        Assert.Equal("t", field.Tag);
        Assert.True(field.IsBinary);
    }

    [Fact]
    public void ExecutionDedup_Properties_RoundTrip()
    {
        var now = DateTime.UtcNow;
        var dedup = new ExecutionDedup
        {
            IdempotencyKey = "key",
            ExecutionId = Guid.NewGuid(),
            CreatedAt = now,
            ExpiresAt = now.AddHours(1)
        };

        Assert.Equal("key", dedup.IdempotencyKey);
        Assert.NotEqual(Guid.Empty, dedup.ExecutionId);
        Assert.Equal(now, dedup.CreatedAt);
        Assert.NotNull(dedup.ExpiresAt);
    }

    [Fact]
    public void ExecutionRecord_Properties_RoundTrip()
    {
        var record = new ExecutionRecord
        {
            Id = Guid.CreateVersion7(),
            WorkflowDefinitionId = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            ParentExecutionId = Guid.NewGuid(),
            StartedAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow.AddMinutes(1),
            Status = ExecutionStatus.Completed,
            NodeRecords = [new NodeExecutionRecord { NodeDefinitionId = "n1" }]
        };

        Assert.NotEqual(Guid.Empty, record.WorkflowDefinitionId);
        Assert.NotNull(record.ProjectId);
        Assert.NotNull(record.ParentExecutionId);
        Assert.Equal(ExecutionStatus.Completed, record.Status);
        Assert.Single(record.NodeRecords);
    }

    [Fact]
    public void LlmMessage_Properties_RoundTrip()
    {
        var message = new LlmMessage
        {
            Role = "assistant",
            Content = "hello",
            ToolCallId = "call-1",
            ToolCalls = [new LlmToolCall { Id = "tc1", Name = "tool", Arguments = "{}" }]
        };

        Assert.Equal("assistant", message.Role);
        Assert.Equal("hello", message.Content);
        Assert.Equal("call-1", message.ToolCallId);
        Assert.Single(message.ToolCalls);
    }

    [Fact]
    public void LlmResponse_Properties_RoundTrip()
    {
        var response = new LlmResponse
        {
            Content = "ok",
            ToolCalls = [new LlmToolCall { Id = "tc", Name = "t" }],
            FinishReason = "stop"
        };

        Assert.Equal("ok", response.Content);
        Assert.True(response.HasToolCalls);
        Assert.Equal("stop", response.FinishReason);
    }

    [Fact]
    public void LlmResponse_NoToolCalls_HasToolCallsIsFalse()
    {
        var response = new LlmResponse();

        Assert.False(response.HasToolCalls);
    }

    [Fact]
    public void LlmStreamChunk_Properties_RoundTrip()
    {
        var chunk = new LlmStreamChunk
        {
            Delta = "hello",
            ToolCalls = [new LlmToolCall { Id = "tc", Name = "t" }],
            IsFinal = true,
            FinishReason = "stop"
        };

        Assert.Equal("hello", chunk.Delta);
        Assert.True(chunk.IsFinal);
        Assert.Equal("stop", chunk.FinishReason);
    }

    [Fact]
    public void LlmToolCall_Properties_RoundTrip()
    {
        var call = new LlmToolCall
        {
            Id = "tc1",
            Name = "tool",
            Arguments = "{\"x\":1}"
        };

        Assert.Equal("tc1", call.Id);
        Assert.Equal("tool", call.Name);
        Assert.Equal("{\"x\":1}", call.Arguments);
    }

    [Fact]
    public void LoopControl_Properties_RoundTrip()
    {
        var control = new LoopControl
        {
            Continue = true,
            IterationIndex = 3,
            NextItem = "item"
        };

        Assert.True(control.Continue);
        Assert.Equal(3, control.IterationIndex);
        Assert.Equal("item", control.NextItem);
    }

    [Fact]
    public void NodeError_Properties_RoundTrip()
    {
        var error = new NodeError
        {
            Code = "E1",
            Message = "msg",
            NodeDefinitionId = "n1",
            Details = new Dictionary<string, string> { ["k"] = "v" },
            StackTrace = "stack"
        };

        Assert.Equal("E1", error.Code);
        Assert.Equal("msg", error.Message);
        Assert.Equal("n1", error.NodeDefinitionId);
        Assert.Equal("v", error.Details["k"]);
        Assert.Equal("stack", error.StackTrace);
    }

    [Fact]
    public void NodeExecutionRecord_Properties_RoundTrip()
    {
        var record = new NodeExecutionRecord
        {
            Id = Guid.NewGuid(),
            NodeDefinitionId = "n1",
            RunIndex = 2,
            StartedAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow.AddSeconds(1),
            Inputs = new Dictionary<string, DataBatch> { ["input"] = new() },
            Output = new NodeExecutionResult { Success = true },
            RawParameters = new Dictionary<string, object> { ["p"] = 1 },
            ResolvedParameters = new Dictionary<string, object> { ["p"] = 2 },
            ParentRecordId = Guid.NewGuid()
        };

        Assert.Equal("n1", record.NodeDefinitionId);
        Assert.Equal(2, record.RunIndex);
        Assert.NotNull(record.CompletedAt);
        Assert.Single(record.Inputs);
        Assert.True(record.Output.Success);
        Assert.Single(record.RawParameters);
        Assert.NotNull(record.ParentRecordId);
    }

    [Fact]
    public void NodeTypeDescriptor_Properties_RoundTrip()
    {
        var descriptor = new NodeTypeDescriptor
        {
            TypeName = "http",
            DisplayName = "HTTP Request",
            Category = "network",
            Icon = "icon",
            ExecutionMode = ExecutionMode.OncePerItem,
            Parameters = [new ParameterDefinition { Name = "url" }],
            Ports = [new PortDefinition { Name = "input" }],
            DefaultIsEntry = true
        };

        Assert.Equal("http", descriptor.TypeName);
        Assert.Equal("HTTP Request", descriptor.DisplayName);
        Assert.Equal(ExecutionMode.OncePerItem, descriptor.ExecutionMode);
        Assert.Single(descriptor.Parameters);
        Assert.Single(descriptor.Ports);
        Assert.True(descriptor.DefaultIsEntry);
    }

    [Fact]
    public void Option_Properties_RoundTrip()
    {
        var option = new Option
        {
            Label = "Yes",
            Value = true
        };

        Assert.Equal("Yes", option.Label);
        Assert.Equal(true, option.Value);
    }

    [Fact]
    public void ParameterDefinition_Properties_RoundTrip()
    {
        var param = new ParameterDefinition
        {
            Name = "url",
            DisplayName = "URL",
            Type = ParameterType.String,
            DefaultValue = "https://example.com",
            Required = true,
            ValidationRules = [new ValidationRule { Type = "required" }],
            DisplayRule = new DisplayRule { Condition = "x" },
            CredentialType = "apiKey",
            Options = [new Option { Label = "A", Value = "a" }],
            Hint = PresentationHint.CodeEditor,
            HintProperties = new Dictionary<string, object> { ["lang"] = "json" },
            Description = "desc",
            ResourceType = "project",
            ItemDefinition = new ParameterDefinition { Name = "item" },
            Fields = [new ParameterDefinition { Name = "field" }]
        };

        Assert.Equal("url", param.Name);
        Assert.Equal("URL", param.DisplayName);
        Assert.Equal(ParameterType.String, param.Type);
        Assert.Equal("https://example.com", param.DefaultValue);
        Assert.True(param.Required);
        Assert.Single(param.ValidationRules);
        Assert.NotNull(param.DisplayRule);
        Assert.Equal("apiKey", param.CredentialType);
        Assert.Single(param.Options);
        Assert.Equal(PresentationHint.CodeEditor, param.Hint);
        Assert.Equal("json", param.HintProperties["lang"]);
        Assert.Equal("desc", param.Description);
        Assert.Equal("project", param.ResourceType);
        Assert.NotNull(param.ItemDefinition);
        Assert.Single(param.Fields);
    }

    [Fact]
    public void PortDefinition_Properties_RoundTrip()
    {
        var port = new PortDefinition
        {
            Name = "input",
            DisplayName = "Input",
            Direction = PortDirection.Input,
            Type = PortType.Main,
            Required = true,
            Condition = "*",
            AllowedTypes = ["string"],
            OutputSchema = new DataSchema { Type = "string" },
            ExpectedSchema = new DataSchema { Type = "object" }
        };

        Assert.Equal("input", port.Name);
        Assert.Equal(PortDirection.Input, port.Direction);
        Assert.Equal(PortType.Main, port.Type);
        Assert.True(port.Required);
        Assert.Equal("*", port.Condition);
        Assert.Single(port.AllowedTypes);
    }

    [Fact]
    public void PortInstance_Properties_RoundTrip()
    {
        var port = new PortInstance
        {
            Name = "output",
            Direction = PortDirection.Output,
            Type = PortType.Main
        };

        Assert.Equal("output", port.Name);
        Assert.Equal(PortDirection.Output, port.Direction);
        Assert.Equal(PortType.Main, port.Type);
    }

    [Fact]
    public void RetryPolicy_Properties_RoundTrip()
    {
        var policy = new RetryPolicy
        {
            MaxRetries = 3,
            BaseDelay = TimeSpan.FromSeconds(1),
            MaxDelay = TimeSpan.FromMinutes(1),
            UseJitter = true,
            BackoffStrategy = BackoffStrategy.Linear,
            RetryableErrorCodes = ["Timeout"]
        };

        Assert.Equal(3, policy.MaxRetries);
        Assert.Equal(TimeSpan.FromSeconds(1), policy.BaseDelay);
        Assert.Equal(TimeSpan.FromMinutes(1), policy.MaxDelay);
        Assert.True(policy.UseJitter);
        Assert.Equal(BackoffStrategy.Linear, policy.BackoffStrategy);
        Assert.Single(policy.RetryableErrorCodes);
    }

    [Fact]
    public void StoredFile_Properties_RoundTrip()
    {
        var file = new StoredFile
        {
            Id = Guid.CreateVersion7(),
            FileName = "doc.pdf",
            ContentType = "application/pdf",
            Size = 1024,
            StoragePath = "/files/doc.pdf",
            ProjectId = Guid.NewGuid(),
            UploadedBy = Guid.NewGuid()
        };

        Assert.Equal("doc.pdf", file.FileName);
        Assert.Equal("application/pdf", file.ContentType);
        Assert.Equal(1024, file.Size);
        Assert.Equal("/files/doc.pdf", file.StoragePath);
        Assert.NotEqual(Guid.Empty, file.ProjectId);
        Assert.NotEqual(Guid.Empty, file.UploadedBy);
    }

    [Fact]
    public void StructuredDiff_Properties_RoundTrip()
    {
        var diff = new StructuredDiff
        {
            Op = "modify",
            NodeId = "n1",
            Field = "name",
            Before = "old",
            After = "new"
        };

        Assert.Equal("modify", diff.Op);
        Assert.Equal("n1", diff.NodeId);
        Assert.Equal("name", diff.Field);
        Assert.Equal("old", diff.Before);
        Assert.Equal("new", diff.After);
    }

    [Fact]
    public void ToolDefinition_Properties_RoundTrip()
    {
        var tool = new ToolDefinition
        {
            Name = "search",
            Description = "search tool",
            ParametersSchema = new { type = "object" },
            TargetNodeDefinitionId = "node-search"
        };

        Assert.Equal("search", tool.Name);
        Assert.Equal("search tool", tool.Description);
        Assert.NotNull(tool.ParametersSchema);
        Assert.Equal("node-search", tool.TargetNodeDefinitionId);
    }

    [Fact]
    public void Trigger_Properties_RoundTrip()
    {
        var trigger = new Trigger
        {
            Id = Guid.CreateVersion7(),
            WorkflowDefinitionId = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            WorkflowVersion = 1,
            Type = TriggerType.Webhook,
            Name = "hook",
            IsActive = true,
            Settings = new TriggerSettings
            {
                WebhookPath = "/hook",
                Secret = "s",
                IsSync = true
            },
            LastTriggeredAt = DateTime.UtcNow,
            NextTriggerAt = DateTime.UtcNow.AddHours(1)
        };

        Assert.Equal(TriggerType.Webhook, trigger.Type);
        Assert.Equal("hook", trigger.Name);
        Assert.True(trigger.IsActive);
        Assert.Equal("/hook", trigger.Settings.WebhookPath);
        Assert.NotNull(trigger.LastTriggeredAt);
        Assert.NotNull(trigger.NextTriggerAt);
    }

    [Fact]
    public void TriggerSettings_Properties_RoundTrip()
    {
        var settings = new TriggerSettings
        {
            CronExpression = "0 * * * *",
            TimeZone = "Asia/Shanghai",
            StartAt = DateTime.UtcNow,
            EndAt = DateTime.UtcNow.AddHours(1),
            WebhookPath = "/hook",
            Secret = "secret",
            AllowedIps = ["127.0.0.1"],
            AllowedOrigins = ["https://example.com"],
            IsSync = true,
            MaxWaitSeconds = 60,
            IntervalSeconds = 120,
            TimeoutSeconds = 45,
            PollNodeId = "poll",
            DedupStrategy = "HashSet",
            SkipIfRunning = false,
            LastPollId = "last",
            LastPollTime = DateTime.UtcNow,
            IdempotencyKeyTemplate = "{headers.x}",
            IdempotencyTtlSeconds = 3600
        };

        Assert.Equal("0 * * * *", settings.CronExpression);
        Assert.Equal("Asia/Shanghai", settings.TimeZone);
        Assert.True(settings.IsSync);
        Assert.Equal(60, settings.MaxWaitSeconds);
        Assert.Equal("HashSet", settings.DedupStrategy);
        Assert.False(settings.SkipIfRunning);
        Assert.Equal(3600, settings.IdempotencyTtlSeconds);
    }

    [Fact]
    public void TriggerSettings_Defaults_AreExpected()
    {
        var settings = new TriggerSettings();

        Assert.Equal(30, settings.MaxWaitSeconds);
        Assert.Equal(60, settings.IntervalSeconds);
        Assert.Equal(30, settings.TimeoutSeconds);
        Assert.Equal("None", settings.DedupStrategy);
        Assert.True(settings.SkipIfRunning);
    }

    [Fact]
    public void ValidationRule_Properties_RoundTrip()
    {
        var rule = new ValidationRule
        {
            Type = "minLength",
            Value = 5,
            ErrorMessage = "too short"
        };

        Assert.Equal("minLength", rule.Type);
        Assert.Equal(5, rule.Value);
        Assert.Equal("too short", rule.ErrorMessage);
    }

    [Fact]
    public void WebhookRoute_Properties_RoundTrip()
    {
        var route = new WebhookRoute
        {
            Id = Guid.CreateVersion7(),
            Path = "/hook",
            Method = "POST",
            WorkflowDefinitionId = Guid.NewGuid(),
            TriggerId = Guid.NewGuid(),
            IsStatic = true,
            Secret = "secret",
            AllowedIps = ["127.0.0.1"],
            AllowedOrigins = ["https://example.com"],
            IsSync = true,
            MaxWaitSeconds = 60
        };

        Assert.Equal("/hook", route.Path);
        Assert.Equal("POST", route.Method);
        Assert.True(route.IsStatic);
        Assert.Equal("secret", route.Secret);
        Assert.Single(route.AllowedIps);
        Assert.True(route.IsSync);
        Assert.Equal(60, route.MaxWaitSeconds);
    }

    [Fact]
    public void WorkflowStyleSettings_Properties_RoundTrip()
    {
        var style = new WorkflowStyleSettings
        {
            LayoutDirection = "horizontal"
        };

        Assert.Equal("horizontal", style.LayoutDirection);
    }

    [Fact]
    public void WorkflowStyleSettings_Default_IsVertical()
    {
        var style = new WorkflowStyleSettings();

        Assert.Equal("vertical", style.LayoutDirection);
    }
}

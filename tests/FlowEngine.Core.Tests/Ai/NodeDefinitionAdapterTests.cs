using System.Text.Json.Nodes;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Ai;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Scripting;

namespace FlowEngine.Core.Tests.Ai;

/// <summary>
/// 验证 <see cref="NodeDefinitionAdapter"/> 的 AI-native 适配行为，
/// 重点覆盖 task-013 的 P4（isTrigger 误标）与 P7（Script/Code 默认值序列化）。
/// </summary>
public class NodeDefinitionAdapterTests
{
    private sealed class FakeNodeType : INodeType
    {
        public string TypeName { get; init; } = "fake";
        public string DisplayName { get; init; } = "Fake";
        public string Category { get; init; } = "AI";
        public string Icon { get; init; } = "";
        public ExecutionMode ExecutionMode { get; init; } = ExecutionMode.OnceForAll;
        public IReadOnlyList<PortDefinition> Ports { get; init; } = [];
        public bool DefaultIsEntry { get; init; }

        public Task<NodeExecutionResult> ExecuteAsync(
            NodeExecutionContext context,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new NodeExecutionResult());
    }

    private static NodeTypeDescriptor DescriptorFor(
        string typeName,
        IReadOnlyList<ParameterDefinition> parameters,
        IReadOnlyList<PortDefinition> ports) =>
        new()
        {
            TypeName = typeName,
            DisplayName = typeName,
            Category = "AI",
            ExecutionMode = ExecutionMode.OnceForAll,
            Parameters = parameters,
            Ports = ports,
        };

    // P4：DefaultIsEntry=true 的非触发器节点（如 llm）不得被误标为触发器。
    [Fact]
    public void ToAiDefinition_NonTriggerNode_WithDefaultIsEntryTrue_IsNotTrigger()
    {
        var node = new FakeNodeType { TypeName = "llm", Category = "AI", DefaultIsEntry = true };
        var descriptor = DescriptorFor("llm", [], []);

        var def = NodeDefinitionAdapter.ToAiDefinition(node, descriptor);

        Assert.False(def.IsTrigger);
    }

    // P4：类别为 Trigger 的节点应被识别为触发器。
    [Fact]
    public void ToAiDefinition_TriggerCategoryNode_IsTrigger()
    {
        var node = new FakeNodeType { TypeName = "manualTrigger", Category = "Trigger", DefaultIsEntry = true };
        var descriptor = DescriptorFor("manualTrigger", [], []);

        var def = NodeDefinitionAdapter.ToAiDefinition(node, descriptor);

        Assert.True(def.IsTrigger);
    }

    // P4：无 IAiDefinitionProvider 覆盖时，节点描述不得误用参数描述。
    [Fact]
    public void ToAiDefinition_WithoutOverride_UsesNodeDisplayNameAsDescription()
    {
        var node = new FakeNodeType { TypeName = "unknown", DisplayName = "大模型", Category = "AI" };
        var descriptor = DescriptorFor("unknown", [], []);

        var def = NodeDefinitionAdapter.ToAiDefinition(node, descriptor);

        Assert.Equal("大模型 节点", def.Description);
    }

    // P7：Script 参数默认值含 Source 时，schema 的 default 应为纯字符串而非 {"source":""} 对象。
    [Fact]
    public void ToAiDefinition_ScriptParameter_WithSource_DefaultRendersAsString()
    {
        var param = new ParameterDefinition
        {
            Name = "Code",
            DisplayName = "代码",
            Type = ParameterType.Script,
            DefaultValue = new Script { Source = "return 1;" },
        };
        var node = new FakeNodeType { TypeName = "script" };
        var descriptor = DescriptorFor("script", [param], []);

        var def = NodeDefinitionAdapter.ToAiDefinition(node, descriptor);

        var props = (JsonObject)((JsonObject)def.InputSchema!)["properties"]!;
        var codeProp = (JsonObject)props["Code"]!;
        Assert.True(codeProp.ContainsKey("default"));
        Assert.Equal("return 1;", codeProp["default"]!.GetValue<string>());
    }

    // P7：Code 参数默认值同样应渲染为字符串。
    [Fact]
    public void ToAiDefinition_CodeParameter_WithSource_DefaultRendersAsString()
    {
        var param = new ParameterDefinition
        {
            Name = "Source",
            DisplayName = "源码",
            Type = ParameterType.Code,
            DefaultValue = new Script { Source = "SELECT 1" },
        };
        var node = new FakeNodeType { TypeName = "sql" };
        var descriptor = DescriptorFor("sql", [param], []);

        var def = NodeDefinitionAdapter.ToAiDefinition(node, descriptor);

        var props = (JsonObject)((JsonObject)def.InputSchema!)["properties"]!;
        var codeProp = (JsonObject)props["Source"]!;
        Assert.Equal("SELECT 1", codeProp["default"]!.GetValue<string>());
    }

    // P7：Script 参数默认值为空时，不应写入 default 键，避免 schema 出现 {"source":""}。
    [Fact]
    public void ToAiDefinition_ScriptParameter_EmptySource_HasNoDefaultKey()
    {
        var param = new ParameterDefinition
        {
            Name = "Code",
            DisplayName = "代码",
            Type = ParameterType.Script,
            DefaultValue = new Script { Source = "" },
        };
        var node = new FakeNodeType { TypeName = "script" };
        var descriptor = DescriptorFor("script", [param], []);

        var def = NodeDefinitionAdapter.ToAiDefinition(node, descriptor);

        var props = (JsonObject)((JsonObject)def.InputSchema!)["properties"]!;
        var codeProp = (JsonObject)props["Code"]!;
        Assert.False(codeProp.ContainsKey("default"));
    }

    // P7：Script 参数无默认值时，不应写入 default 键。
    [Fact]
    public void ToAiDefinition_ScriptParameter_WithoutDefault_HasNoDefaultKey()
    {
        var param = new ParameterDefinition
        {
            Name = "Code",
            DisplayName = "代码",
            Type = ParameterType.Script,
        };
        var node = new FakeNodeType { TypeName = "script" };
        var descriptor = DescriptorFor("script", [param], []);

        var def = NodeDefinitionAdapter.ToAiDefinition(node, descriptor);

        var props = (JsonObject)((JsonObject)def.InputSchema!)["properties"]!;
        var codeProp = (JsonObject)props["Code"]!;
        Assert.False(codeProp.ContainsKey("default"));
    }

    // P5a 对齐：带默认值的必填参数不应出现在 schema 的 required 数组中，
    // 否则 AI 会收到与后端校验（P5a 允许省略）矛盾的「必填」提示。
    [Fact]
    public void ToAiDefinition_RequiredParameterWithDefault_NotInRequiredArray()
    {
        var param = new ParameterDefinition
        {
            Name = "channel",
            DisplayName = "渠道",
            Type = ParameterType.Options,
            Required = true,
            DefaultValue = "email",
        };
        var node = new FakeNodeType { TypeName = "notify" };
        var descriptor = DescriptorFor("notify", [param], []);

        var def = NodeDefinitionAdapter.ToAiDefinition(node, descriptor);

        var required = (JsonArray)((JsonObject)def.InputSchema!)["required"]!;
        Assert.DoesNotContain(required, v => v!.GetValue<string>() == "channel");
    }

    // 对照：无默认值的必填参数仍应出现在 required 数组中。
    [Fact]
    public void ToAiDefinition_RequiredParameterWithoutDefault_InRequiredArray()
    {
        var param = new ParameterDefinition
        {
            Name = "url",
            DisplayName = "URL",
            Type = ParameterType.String,
            Required = true,
        };
        var node = new FakeNodeType { TypeName = "httpRequest" };
        var descriptor = DescriptorFor("httpRequest", [param], []);

        var def = NodeDefinitionAdapter.ToAiDefinition(node, descriptor);

        var required = (JsonArray)((JsonObject)def.InputSchema!)["required"]!;
        Assert.Contains(required, v => v!.GetValue<string>() == "url");
    }

    [Fact]
    public void ToAiDefinition_ExpressionLanguage_IsJavascript()
    {
        var node = new FakeNodeType { TypeName = "httpRequest" };
        var descriptor = DescriptorFor("httpRequest", [], []);

        var def = NodeDefinitionAdapter.ToAiDefinition(node, descriptor);

        Assert.Equal("javascript", def.ExpressionLanguage);
    }

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
        var connParam = new ParameterDefinition
        {
            Name = "connection",
            Type = ParameterType.Credential,
            CredentialType = "database",
        };
        var descriptor = DescriptorFor("dbUpsert", [authParam, connParam], []);

        var schema = NodeDefinitionAdapter.BuildInputSchema(descriptor);
        var authProp = schema["properties"]!["authentication"]!;
        Assert.NotNull(authProp["credentialFieldMapping"]);

        var connProp = schema["properties"]!["connection"]!;
        Assert.Equal("database", connProp["credentialType"]?.GetValue<string>());
    }

    [Fact]
    public void BuildInputSchema_ScriptParameter_HasExpressionMeta()
    {
        var param = new ParameterDefinition
        {
            Name = "url",
            Type = ParameterType.Script,
            Hint = PresentationHint.Expression,
            Description = "Target URL",
        };
        var descriptor = new NodeTypeDescriptor
        {
            TypeName = "httpRequest",
            Parameters = [param],
            Ports = [],
        };

        var schema = NodeDefinitionAdapter.BuildInputSchema(descriptor);
        var urlProp = schema["properties"]!["url"]!;

        Assert.Equal("javascript", urlProp["expressionLanguage"]?.GetValue<string>());
        Assert.NotNull(urlProp["antiPatterns"]);
        Assert.NotNull(urlProp["examples"]);
    }
}

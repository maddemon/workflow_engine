using System.Text.Json.Nodes;
using FlowEngine.Application.Workflows;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Ai;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;

namespace FlowEngine.Application.Tests.Workflows;

/// <summary>
/// AI 节点目录服务测试。
/// </summary>
public sealed class CatalogServiceTests
{
    private static readonly NodeTypeDescriptor StringDescriptor = new()
    {
        TypeName = "testString",
        DisplayName = "Test String",
        Category = "Core",
        Icon = "string-icon",
        Parameters =
        [
            new ParameterDefinition
            {
                Name = "input",
                DisplayName = "Input",
                Type = ParameterType.String,
                Required = true,
                Description = "A string input",
            },
        ],
        Ports =
        [
            new PortDefinition
            {
                Name = "output",
                DisplayName = "输出",
                Direction = PortDirection.Output,
            },
        ],
    };

    private static readonly NodeTypeDescriptor NumberDescriptor = new()
    {
        TypeName = "testNumber",
        DisplayName = "Test Number",
        Category = "Math",
        Parameters =
        [
            new ParameterDefinition
            {
                Name = "value",
                DisplayName = "Value",
                Type = ParameterType.Number,
                Required = false,
            },
        ],
        Ports = [],
    };

    private static readonly NodeTypeDescriptor BooleanDescriptor = new()
    {
        TypeName = "testBoolean",
        DisplayName = "Test Boolean",
        Category = "Logic",
        Parameters =
        [
            new ParameterDefinition
            {
                Name = "flag",
                DisplayName = "Flag",
                Type = ParameterType.Boolean,
                Required = true,
            },
        ],
        Ports = [],
    };

    private static readonly NodeTypeDescriptor ArrayParamDescriptor = new()
    {
        TypeName = "testArray",
        DisplayName = "Test Array",
        Category = "Data",
        Parameters =
        [
            new ParameterDefinition
            {
                Name = "items",
                DisplayName = "Items",
                Type = ParameterType.Array,
                Required = true,
                ItemDefinition = new ParameterDefinition
                {
                    Name = "item",
                    Type = ParameterType.String,
                },
            },
        ],
        Ports = [],
    };

    private static readonly NodeTypeDescriptor CodeParamDescriptor = new()
    {
        TypeName = "testCode",
        DisplayName = "Test Code",
        Category = "Script",
        Parameters =
        [
            new ParameterDefinition
            {
                Name = "script",
                DisplayName = "Script",
                Type = ParameterType.Code,
                Required = true,
            },
        ],
        Ports = [],
    };

    private static readonly NodeTypeDescriptor CredentialParamDescriptor = new()
    {
        TypeName = "testCredential",
        DisplayName = "Test Credential",
        Category = "Core",
        Parameters =
        [
            new ParameterDefinition
            {
                Name = "apiKey",
                DisplayName = "API Key",
                Type = ParameterType.Credential,
                Required = true,
                DefaultValue = "should-be-omitted",
            },
            new ParameterDefinition
            {
                Name = "secretKey",
                DisplayName = "Secret Key",
                Type = ParameterType.String,
                Required = false,
                DefaultValue = "also-omitted",
            },
        ],
        Ports = [],
    };

    private static readonly NodeTypeDescriptor OptionsParamDescriptor = new()
    {
        TypeName = "testOptions",
        DisplayName = "Test Options",
        Category = "Core",
        Parameters =
        [
            new ParameterDefinition
            {
                Name = "choice",
                DisplayName = "Choice",
                Type = ParameterType.Options,
                Required = true,
                Options =
                [
                    new Option { Label = "A", Value = "a" },
                    new Option { Label = "B", Value = "b" },
                ],
            },
        ],
        Ports = [],
    };

    private static readonly NodeTypeDescriptor DefaultValueDescriptor = new()
    {
        TypeName = "testWithDefault",
        DisplayName = "Test With Default",
        Category = "Core",
        Parameters =
        [
            new ParameterDefinition
            {
                Name = "timeout",
                DisplayName = "Timeout",
                Type = ParameterType.Number,
                Required = false,
                DefaultValue = 30,
            },
        ],
        Ports = [],
    };

    private static readonly NodeTypeDescriptor TriggerDescriptor = new()
    {
        TypeName = "testTrigger",
        DisplayName = "Test Trigger",
        Category = "Trigger",
        Parameters = [],
        Ports = [],
        DefaultIsEntry = true,
    };

    private static readonly NodeTypeDescriptor EntryDescriptor = new()
    {
        TypeName = "testEntry",
        DisplayName = "Test Entry",
        Category = "Core",
        Parameters = [],
        Ports = [],
        DefaultIsEntry = true,
    };

    private static readonly NodeTypeDescriptor JsonParamDescriptor = new()
    {
        TypeName = "testJson",
        DisplayName = "Test JSON",
        Category = "Core",
        Parameters =
        [
            new ParameterDefinition
            {
                Name = "config",
                DisplayName = "Config",
                Type = ParameterType.Json,
                Required = true,
                Description = "JSON config",
            },
        ],
        Ports = [],
    };

    private static readonly NodeTypeDescriptor ScriptParamDescriptor = new()
    {
        TypeName = "testScript",
        DisplayName = "Test Script",
        Category = "Script",
        Parameters =
        [
            new ParameterDefinition
            {
                Name = "expression",
                DisplayName = "Expression",
                Type = ParameterType.Script,
                Required = false,
            },
        ],
        Ports = [],
    };

    #region Test node types

    private sealed class TestStringNode : INodeType
    {
        public string TypeName => "testString";
        public string DisplayName => "Test String";
        public string Category => "Core";
        public string Icon => "string-icon";
        public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;
        public IReadOnlyList<PortDefinition> Ports { get; } =
        [
            new PortDefinition { Name = "output", DisplayName = "输出", Direction = PortDirection.Output },
        ];
        public bool DefaultIsEntry => false;
        public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default)
            => Task.FromResult(new NodeExecutionResult { Success = true });
    }

    private sealed class TestNumberNode : INodeType
    {
        public string TypeName => "testNumber";
        public string DisplayName => "Test Number";
        public string Category => "Math";
        public string Icon => "number-icon";
        public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;
        public IReadOnlyList<PortDefinition> Ports => [];
        public bool DefaultIsEntry => false;
        public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default)
            => Task.FromResult(new NodeExecutionResult { Success = true });
    }

    private sealed class TestBooleanNode : INodeType
    {
        public string TypeName => "testBoolean";
        public string DisplayName => "Test Boolean";
        public string Category => "Logic";
        public string Icon => "bool-icon";
        public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;
        public IReadOnlyList<PortDefinition> Ports => [];
        public bool DefaultIsEntry => false;
        public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default)
            => Task.FromResult(new NodeExecutionResult { Success = true });
    }

    private sealed class TestArrayNode : INodeType
    {
        public string TypeName => "testArray";
        public string DisplayName => "Test Array";
        public string Category => "Data";
        public string Icon => "array-icon";
        public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;
        public IReadOnlyList<PortDefinition> Ports => [];
        public bool DefaultIsEntry => false;
        public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default)
            => Task.FromResult(new NodeExecutionResult { Success = true });
    }

    private sealed class TestCodeNode : INodeType
    {
        public string TypeName => "testCode";
        public string DisplayName => "Test Code";
        public string Category => "Script";
        public string Icon => "code-icon";
        public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;
        public IReadOnlyList<PortDefinition> Ports => [];
        public bool DefaultIsEntry => false;
        public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default)
            => Task.FromResult(new NodeExecutionResult { Success = true });
    }

    private sealed class TestCredentialNode : INodeType
    {
        public string TypeName => "testCredential";
        public string DisplayName => "Test Credential";
        public string Category => "Core";
        public string Icon => "credential-icon";
        public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;
        public IReadOnlyList<PortDefinition> Ports => [];
        public bool DefaultIsEntry => false;
        public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default)
            => Task.FromResult(new NodeExecutionResult { Success = true });
    }

    private sealed class TestOptionsNode : INodeType
    {
        public string TypeName => "testOptions";
        public string DisplayName => "Test Options";
        public string Category => "Core";
        public string Icon => "options-icon";
        public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;
        public IReadOnlyList<PortDefinition> Ports => [];
        public bool DefaultIsEntry => false;
        public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default)
            => Task.FromResult(new NodeExecutionResult { Success = true });
    }

    private sealed class TestWithDefaultNode : INodeType
    {
        public string TypeName => "testWithDefault";
        public string DisplayName => "Test With Default";
        public string Category => "Core";
        public string Icon => "default-icon";
        public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;
        public IReadOnlyList<PortDefinition> Ports => [];
        public bool DefaultIsEntry => false;
        public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default)
            => Task.FromResult(new NodeExecutionResult { Success = true });
    }

    private sealed class TestTriggerNode : INodeType
    {
        public string TypeName => "testTrigger";
        public string DisplayName => "Test Trigger";
        public string Category => "Trigger";
        public string Icon => "trigger-icon";
        public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;
        public IReadOnlyList<PortDefinition> Ports => [];
        public bool DefaultIsEntry => true;
        public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default)
            => Task.FromResult(new NodeExecutionResult { Success = true });
    }

    private sealed class TestEntryNode : INodeType
    {
        public string TypeName => "testEntry";
        public string DisplayName => "Test Entry";
        public string Category => "Core";
        public string Icon => "entry-icon";
        public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;
        public IReadOnlyList<PortDefinition> Ports => [];
        public bool DefaultIsEntry => true;
        public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default)
            => Task.FromResult(new NodeExecutionResult { Success = true });
    }

    private sealed class TestJsonNode : INodeType
    {
        public string TypeName => "testJson";
        public string DisplayName => "Test JSON";
        public string Category => "Core";
        public string Icon => "json-icon";
        public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;
        public IReadOnlyList<PortDefinition> Ports => [];
        public bool DefaultIsEntry => false;
        public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default)
            => Task.FromResult(new NodeExecutionResult { Success = true });
    }

    private sealed class TestScriptNode : INodeType
    {
        public string TypeName => "testScript";
        public string DisplayName => "Test Script";
        public string Category => "Script";
        public string Icon => "script-icon";
        public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;
        public IReadOnlyList<PortDefinition> Ports => [];
        public bool DefaultIsEntry => false;
        public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default)
            => Task.FromResult(new NodeExecutionResult { Success = true });
    }

    #endregion

    #region IAiDefinitionProvider test node

    private sealed class TestOverrideNode : INodeType, IAiDefinitionProvider
    {
        public string TypeName => "testOverride";
        public string DisplayName => "Test Override";
        public string Category => "Custom";
        public string Icon => "override-icon";
        public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;
        public IReadOnlyList<PortDefinition> Ports => [];
        public bool DefaultIsEntry => false;

        public AiNodeDefinition GetAiDefinition(NodeTypeDescriptor descriptor)
        {
            return new AiNodeDefinition
            {
                Name = TypeName,
                DisplayName = DisplayName,
                Category = Category,
                Description = "自定义描述",
                Tags = ["custom", "override"],
                IsTrigger = false,
                InputSchema = new JsonObject { ["type"] = "object" },
                OutputSchema = new JsonObject { ["type"] = "object", ["description"] = "自定义输出" },
                Ports =
                [
                    new AiPortSchema { Name = "customOut", Direction = "Output", Description = "自定义输出端口" },
                ],
                Examples =
                [
                    new AiExample
                    {
                        Description = "示例",
                        Input = new JsonObject { ["foo"] = "bar" },
                        Output = new JsonObject { ["result"] = "ok" },
                    },
                ],
            };
        }

        public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default)
            => Task.FromResult(new NodeExecutionResult { Success = true });
    }

    private static readonly NodeTypeDescriptor OverrideDescriptor = new()
    {
        TypeName = "testOverride",
        DisplayName = "Test Override",
        Category = "Trigger",
        Parameters = [],
        Ports = [new PortDefinition { Name = "default", Direction = PortDirection.Output, DisplayName = "默认" }],
    };

    #endregion

    [Fact]
    public void ListAll_Returns_All_Nodes_With_Required_Summary_Fields()
    {
        var registry = CreateFullRegistry();
        var service = new CatalogService(registry);

        var results = service.ListAll();

        Assert.NotEmpty(results);
        foreach (var summary in results)
        {
            Assert.False(string.IsNullOrEmpty(summary.Name));
            Assert.False(string.IsNullOrEmpty(summary.DisplayName));
            Assert.False(string.IsNullOrEmpty(summary.Description));
            Assert.False(string.IsNullOrEmpty(summary.Category));
            Assert.NotNull(summary.Tags);
            // IsTrigger can be true or false
        }
    }

    [Fact]
    public void ListAll_Returns_Correct_Count()
    {
        var registry = CreateFullRegistry();
        var service = new CatalogService(registry);

        var results = service.ListAll();

        // testOverride + testString + testNumber + testBoolean + testArray + testCode +
        // testCredential + testOptions + testWithDefault + testTrigger + testEntry + testJson + testScript
        Assert.Equal(13, results.Count);
    }

    [Fact]
    public void GetByName_Returns_Null_For_Unknown()
    {
        var registry = new StubNodeRegistry([], []);
        var service = new CatalogService(registry);

        var result = service.GetByName("nonExistent");

        Assert.Null(result);
    }

    [Fact]
    public void GetByName_Returns_Detail_With_InputSchema()
    {
        var registry = CreateFullRegistry();
        var service = new CatalogService(registry);

        var result = service.GetByName("testString");

        Assert.NotNull(result);
        Assert.Equal("testString", result.Name);
        Assert.NotNull(result.InputSchema);
        var inputSchema = result.InputSchema.AsObject();
        Assert.Equal("object", inputSchema["type"]?.ToString());
        Assert.NotNull(inputSchema["properties"]);
        Assert.NotNull(inputSchema["required"]);
    }

    [Fact]
    public void TypeMapping_String_Produces_String_Schema()
    {
        var definition = NodeDefinitionAdapter.ToAiDefinition(new TestStringNode(), StringDescriptor);
        var inputSchema = definition.InputSchema!.AsObject();
        var properties = (JsonObject)inputSchema["properties"]!;
        var inputProp = properties["input"]!.AsObject();

        Assert.Equal("string", inputProp["type"]?.ToString());
    }

    [Fact]
    public void TypeMapping_Number_Produces_Number_Schema()
    {
        var definition = NodeDefinitionAdapter.ToAiDefinition(new TestNumberNode(), NumberDescriptor);
        var inputSchema = definition.InputSchema!.AsObject();
        var properties = (JsonObject)inputSchema["properties"]!;
        var valueProp = properties["value"]!.AsObject();

        Assert.Equal("number", valueProp["type"]?.ToString());
    }

    [Fact]
    public void TypeMapping_Boolean_Produces_Boolean_Schema()
    {
        var definition = NodeDefinitionAdapter.ToAiDefinition(new TestBooleanNode(), BooleanDescriptor);
        var inputSchema = definition.InputSchema!.AsObject();
        var properties = (JsonObject)inputSchema["properties"]!;
        var flagProp = properties["flag"]!.AsObject();

        Assert.Equal("boolean", flagProp["type"]?.ToString());
    }

    [Fact]
    public void TypeMapping_Array_Produces_Array_Schema()
    {
        var definition = NodeDefinitionAdapter.ToAiDefinition(new TestArrayNode(), ArrayParamDescriptor);
        var inputSchema = definition.InputSchema!.AsObject();
        var properties = (JsonObject)inputSchema["properties"]!;
        var itemsProp = properties["items"]!.AsObject();

        Assert.Equal("array", itemsProp["type"]?.ToString());
        Assert.NotNull(itemsProp["items"]);
    }

    [Fact]
    public void TypeMapping_Code_Produces_String_Schema()
    {
        var definition = NodeDefinitionAdapter.ToAiDefinition(new TestCodeNode(), CodeParamDescriptor);
        var inputSchema = definition.InputSchema!.AsObject();
        var properties = (JsonObject)inputSchema["properties"]!;
        var scriptProp = properties["script"]!.AsObject();

        Assert.Equal("string", scriptProp["type"]?.ToString());
    }

    [Fact]
    public void SupportsExpression_Is_True_For_String_Json_Code_Script()
    {
        // String
        var def = NodeDefinitionAdapter.ToAiDefinition(new TestStringNode(), StringDescriptor);
        var inputSchema = def.InputSchema!.AsObject();
        var props = (JsonObject)inputSchema["properties"]!;
        Assert.True((bool)props["input"]!["supportsExpression"]!);

        // Json
        def = NodeDefinitionAdapter.ToAiDefinition(new TestJsonNode(), JsonParamDescriptor);
        inputSchema = def.InputSchema!.AsObject();
        props = (JsonObject)inputSchema["properties"]!;
        Assert.True((bool)props["config"]!["supportsExpression"]!);

        // Code
        def = NodeDefinitionAdapter.ToAiDefinition(new TestCodeNode(), CodeParamDescriptor);
        inputSchema = def.InputSchema!.AsObject();
        props = (JsonObject)inputSchema["properties"]!;
        Assert.True((bool)props["script"]!["supportsExpression"]!);

        // Script
        def = NodeDefinitionAdapter.ToAiDefinition(new TestScriptNode(), ScriptParamDescriptor);
        inputSchema = def.InputSchema!.AsObject();
        props = (JsonObject)inputSchema["properties"]!;
        Assert.True((bool)props["expression"]!["supportsExpression"]!);
    }

    [Fact]
    public void SupportsExpression_Is_False_For_Number_Boolean_Credential()
    {
        // Number
        var def = NodeDefinitionAdapter.ToAiDefinition(new TestNumberNode(), NumberDescriptor);
        var inputSchema = def.InputSchema!.AsObject();
        var props = (JsonObject)inputSchema["properties"]!;
        Assert.False(props["value"]!.AsObject().ContainsKey("supportsExpression"));

        // Boolean
        def = NodeDefinitionAdapter.ToAiDefinition(new TestBooleanNode(), BooleanDescriptor);
        inputSchema = def.InputSchema!.AsObject();
        props = (JsonObject)inputSchema["properties"]!;
        Assert.False(props["flag"]!.AsObject().ContainsKey("supportsExpression"));

        // Credential (but the param is Credential type, should not have supportsExpression)
        def = NodeDefinitionAdapter.ToAiDefinition(new TestCredentialNode(), CredentialParamDescriptor);
        inputSchema = def.InputSchema!.AsObject();
        props = (JsonObject)inputSchema["properties"]!;
        Assert.False(props["apiKey"]!.AsObject().ContainsKey("supportsExpression"));
    }

    [Fact]
    public void Required_Parameter_Appears_In_Required_Array()
    {
        var definition = NodeDefinitionAdapter.ToAiDefinition(new TestStringNode(), StringDescriptor);
        var inputSchema = definition.InputSchema!.AsObject();
        var required = (JsonArray)inputSchema["required"]!;

        Assert.Contains(required, n => n?.ToString() == "input");
    }

    [Fact]
    public void NonRequired_Parameter_Not_In_Required_Array()
    {
        var definition = NodeDefinitionAdapter.ToAiDefinition(new TestNumberNode(), NumberDescriptor);
        var inputSchema = definition.InputSchema!.AsObject();
        var required = (JsonArray)inputSchema["required"]!;

        Assert.DoesNotContain(required, n => n?.ToString() == "value");
    }

    [Fact]
    public void Sensitive_Param_Default_Is_Omitted()
    {
        var definition = NodeDefinitionAdapter.ToAiDefinition(new TestCredentialNode(), CredentialParamDescriptor);
        var inputSchema = definition.InputSchema!.AsObject();
        var props = (JsonObject)inputSchema["properties"]!;

        // Credential type - default omitted
        var apiKeyProp = props["apiKey"]!.AsObject();
        Assert.False(apiKeyProp.ContainsKey("default"));

        // Name contains "secret" - default omitted even though type is String
        var secretProp = props["secretKey"]!.AsObject();
        Assert.False(secretProp.ContainsKey("default"));
    }

    [Fact]
    public void NonSensitive_Default_Is_Included()
    {
        var definition = NodeDefinitionAdapter.ToAiDefinition(new TestWithDefaultNode(), DefaultValueDescriptor);
        var inputSchema = definition.InputSchema!.AsObject();
        var props = (JsonObject)inputSchema["properties"]!;
        var timeoutProp = props["timeout"]!.AsObject();

        Assert.True(timeoutProp.ContainsKey("default"));
    }

    [Fact]
    public void IsTrigger_True_For_Trigger_Category()
    {
        var definition = NodeDefinitionAdapter.ToAiDefinition(new TestTriggerNode(), TriggerDescriptor);

        Assert.True(definition.IsTrigger);
    }

    [Fact]
    public void IsTrigger_True_For_DefaultIsEntry()
    {
        var definition = NodeDefinitionAdapter.ToAiDefinition(new TestEntryNode(), EntryDescriptor);

        Assert.True(definition.IsTrigger);
    }

    [Fact]
    public void IsTrigger_False_For_Normal_Node()
    {
        var definition = NodeDefinitionAdapter.ToAiDefinition(new TestStringNode(), StringDescriptor);

        Assert.False(definition.IsTrigger);
    }

    [Fact]
    public void Ports_Are_Converted_Correctly()
    {
        var definition = NodeDefinitionAdapter.ToAiDefinition(new TestStringNode(), StringDescriptor);

        Assert.Single(definition.Ports);
        Assert.Equal("output", definition.Ports[0].Name);
        Assert.Equal("Output", definition.Ports[0].Direction);
        Assert.Equal("输出", definition.Ports[0].Description);
    }

    [Fact]
    public void Options_Produces_Enum_Schema()
    {
        var definition = NodeDefinitionAdapter.ToAiDefinition(new TestOptionsNode(), OptionsParamDescriptor);
        var inputSchema = definition.InputSchema!.AsObject();
        var props = (JsonObject)inputSchema["properties"]!;
        var choiceProp = props["choice"]!.AsObject();

        Assert.Equal("string", choiceProp["type"]?.ToString());
        Assert.NotNull(choiceProp["enum"]);
    }

    [Fact]
    public void Override_Provider_Returns_Custom_Description_Tags_Examples()
    {
        var definition = NodeDefinitionAdapter.ToAiDefinition(new TestOverrideNode(), OverrideDescriptor);

        Assert.Equal("自定义描述", definition.Description);
        Assert.Contains("custom", definition.Tags);
        Assert.Contains("override", definition.Tags);
        Assert.Single(definition.Examples);
        Assert.Equal("示例", definition.Examples[0].Description);
    }

    [Fact]
    public void Override_Provider_Custom_OutputSchema()
    {
        var definition = NodeDefinitionAdapter.ToAiDefinition(new TestOverrideNode(), OverrideDescriptor);

        Assert.NotNull(definition.OutputSchema);
        Assert.Equal("object", definition.OutputSchema["type"]?.ToString());
        Assert.Equal("自定义输出", definition.OutputSchema["description"]?.ToString());
    }

    [Fact]
    public void Override_Provider_Adopts_All_Fields_Over_AutoDerivation()
    {
        // 设计 §3.4：IAiDefinitionProvider.GetAiDefinition() 优先级高于自动推导。
        // OverrideDescriptor 的 Category=Trigger（推导 IsTrigger=true、端口 default），
        // 但覆盖定义应优先采用。
        var definition = NodeDefinitionAdapter.ToAiDefinition(new TestOverrideNode(), OverrideDescriptor);

        Assert.Equal("Custom", definition.Category);       // 覆盖（非 descriptor 的 Trigger）
        Assert.False(definition.IsTrigger);                 // 覆盖（非推导的 true）
        Assert.Equal("Test Override", definition.DisplayName);
        Assert.NotNull(definition.InputSchema);
        Assert.Null(definition.InputSchema["properties"]); // 覆盖（自动推导会含空 properties）
        Assert.NotEmpty(definition.Ports);
        Assert.Equal("customOut", definition.Ports[0].Name); // 覆盖（非 descriptor 的 default）
    }

    [Fact]
    public void GetByName_Returns_Detail_With_Ports()
    {
        var registry = CreateFullRegistry();
        var service = new CatalogService(registry);

        var result = service.GetByName("testString");

        Assert.NotNull(result);
        Assert.NotEmpty(result.Ports);
        Assert.Equal("output", result.Ports[0].Name);
    }

    [Fact]
    public void GetByName_Returns_Detail_With_Examples()
    {
        var registry = CreateFullRegistry();
        var service = new CatalogService(registry);

        var result = service.GetByName("testOverride");

        Assert.NotNull(result);
        Assert.Single(result.Examples);
    }

    [Fact]
    public void GetByName_Returns_Detail_With_OutputSchema()
    {
        var registry = CreateFullRegistry();
        var service = new CatalogService(registry);

        var result = service.GetByName("testOverride");

        Assert.NotNull(result);
        Assert.NotNull(result.OutputSchema);
    }

    private static StubNodeRegistry CreateFullRegistry()
    {
        var types = new List<(INodeType, NodeTypeDescriptor)>
        {
            (new TestStringNode(), StringDescriptor),
            (new TestNumberNode(), NumberDescriptor),
            (new TestBooleanNode(), BooleanDescriptor),
            (new TestArrayNode(), ArrayParamDescriptor),
            (new TestCodeNode(), CodeParamDescriptor),
            (new TestCredentialNode(), CredentialParamDescriptor),
            (new TestOptionsNode(), OptionsParamDescriptor),
            (new TestWithDefaultNode(), DefaultValueDescriptor),
            (new TestTriggerNode(), TriggerDescriptor),
            (new TestEntryNode(), EntryDescriptor),
            (new TestJsonNode(), JsonParamDescriptor),
            (new TestScriptNode(), ScriptParamDescriptor),
            (new TestOverrideNode(), OverrideDescriptor),
        };

        return new StubNodeRegistry(
            types.Select(t => t.Item1).ToList(),
            types.Select(t => t.Item2).ToList());
    }

    private sealed class StubNodeRegistry(
        IReadOnlyCollection<INodeType> nodeTypes,
        IReadOnlyCollection<NodeTypeDescriptor> descriptors) : INodeRegistry
    {
        public void Register(INodeType nodeType) { }
        public INodeType Get(string typeName) =>
            nodeTypes.First(n => n.TypeName.Equals(typeName, StringComparison.OrdinalIgnoreCase));
        public bool TryGet(string typeName, out INodeType? nodeType)
        {
            nodeType = nodeTypes.FirstOrDefault(n =>
                n.TypeName.Equals(typeName, StringComparison.OrdinalIgnoreCase));
            return nodeType is not null;
        }

        public IReadOnlyCollection<INodeType> GetAll() => nodeTypes;
        public INodeType CreateInstance(string typeName) => Get(typeName);
        public IReadOnlyCollection<NodeTypeDescriptor> GetDescriptors() => descriptors;
        public NodeTypeDescriptor GetDescriptor(string typeName) =>
            descriptors.First(d => d.TypeName.Equals(typeName, StringComparison.OrdinalIgnoreCase));
    }
}

using System.Reflection;
using System.Text.Json.Serialization;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Attributes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Scripting;
using FlowEngine.Runtime.Registry;

namespace FlowEngine.Runtime.Tests.Registry;

/// <summary>
/// 属性过滤测试：验证 <see cref="PropertyFilter.ShouldSkip"/> 对各分支的判定（保留为参数的属性 vs 跳过的属性）。
/// </summary>
public class PropertyFilterTests
{
    private static bool Skip(string propertyName, BindingFlags flags = BindingFlags.Public | BindingFlags.Instance)
        => PropertyFilter.ShouldSkip(typeof(SampleNode).GetProperty(propertyName, flags)!);

    private static bool SkipFromInterface(string propertyName)
        => PropertyFilter.ShouldSkip(typeof(IExtra).GetProperty(propertyName)!);

    [Fact]
    public void ShouldSkip_NormalNodeProperty_ReturnsFalse_KeptAsParameter()
    {
        Assert.False(Skip("NormalProp"));
    }

    [Fact]
    public void ShouldSkip_InheritedFromNonNodeTypeBase_ReturnsFalse_KeptAsParameter()
    {
        // 基类未实现 INodeType，属性声明在基类上：仍应保留为参数属性。
        Assert.False(Skip("BaseProp"));
    }

    [Fact]
    public void ShouldSkip_PropertyWithIgnoreParameterAttribute_ReturnsTrue()
    {
        Assert.True(Skip("IgnoredProp"));
    }

    [Fact]
    public void ShouldSkip_PropertyWithInjectAttribute_ReturnsTrue()
    {
        // 由引擎注入的能力属性（如 NodeExecutionContext）不应出现在用户参数面板。
        Assert.True(Skip("InjectedProp"));
    }

    [Fact]
    public void ShouldSkip_PropertyWithJsonIgnoreAttribute_ReturnsTrue()
    {
        Assert.True(Skip("JsonIgnoredProp"));
    }

    [Fact]
    public void ShouldSkip_InterfacePortsProperty_ReturnsTrue()
    {
        Assert.True(Skip("Ports"));
    }

    [Fact]
    public void ShouldSkip_ReadOnlyProperty_WithoutSetter_ReturnsTrue()
    {
        Assert.True(Skip("ReadOnlyProp"));
    }

    [Fact]
    public void ShouldSkip_WriteOnlyProperty_WithoutGetter_ReturnsTrue()
    {
        Assert.True(Skip("WriteOnlyProp"));
    }

    [Fact]
    public void ShouldSkip_IndexerProperty_ReturnsTrue()
    {
        Assert.True(Skip("Item", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic));
    }

    [Fact]
    public void ShouldSkip_ExplicitInterfaceProperty_DeclaredOnInterface_ReturnsTrue()
    {
        // 显式实现的接口属性：DeclaringType 为接口本身（非 INodeType），
        // 视为非参数属性。
        Assert.True(SkipFromInterface("ExtraProp"));
    }

    [Fact]
    public void ShouldSkip_ImplicitInterfaceProperty_DeclaredOnClass_ReturnsFalse()
    {
        // 隐式实现的接口属性：实际声明在节点类上，应保留为参数属性。
        Assert.False(Skip("ExtraProp"));
    }

    private interface IExtra
    {
        string ExtraProp { get; set; }
    }

    private class BaseNode
    {
        public string BaseProp { get; set; } = string.Empty;
    }

    private sealed class SampleNode : BaseNode, INodeType, IExtra
    {
        [IgnoreParameter]
        public string IgnoredProp { get; set; } = string.Empty;

        [Inject]
        public NodeExecutionContext InjectedProp { get; set; } = null!;

        [JsonIgnore]
        public string JsonIgnoredProp { get; set; } = string.Empty;

        public string NormalProp { get; set; } = string.Empty;

        public string ReadOnlyProp { get; } = "x";

        public string WriteOnlyProp { set { } }

        public string ExtraProp { get; set; } = string.Empty;

        string IExtra.ExtraProp { get; set; } = string.Empty;

        public string this[int index] { get => string.Empty; set { } }

        public string TypeName => "sample";
        public string DisplayName => "Sample";
        public string Category => "Test";
        public string Icon => "test";
        public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;
        public IReadOnlyList<PortDefinition> Ports { get; } = [];
        public bool DefaultIsEntry => false;

        public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default)
            => Task.FromResult(new NodeExecutionResult { Success = true });
    }
}

using System.Reflection;
using FlowEngine.Core.Attributes;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Scripting;

namespace FlowEngine.Core.Tests;

public class AttributesTests
{
    [Fact]
    public void CredentialAttribute_Stores_Types_And_PrimaryType()
    {
        var attr = new CredentialAttribute("apiKey", "oauth2");

        Assert.Equal(["apiKey", "oauth2"], attr.CredentialTypes);
        Assert.Equal("apiKey", attr.CredentialType);
    }

    [Fact]
    public void CredentialAttribute_EmptyTypes_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new CredentialAttribute());
    }

    [Fact]
    public void DisplayConditionAttribute_Stores_PropertyName_And_Value()
    {
        var attr = new DisplayConditionAttribute("Method", "POST");

        Assert.Equal("Method", attr.PropertyName);
        Assert.Equal("POST", attr.Value);
    }

    [Fact]
    public void DisplayConditionAttribute_NullPropertyName_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new DisplayConditionAttribute(null!, "x"));
    }

    [Fact]
    public void DisplayConditionAttribute_NullValue_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new DisplayConditionAttribute("x", null!));
    }

    [Fact]
    public void HintAttribute_Default_HasNoComponent_And_EmptyProperties()
    {
        var attr = new HintAttribute();

        Assert.Null(attr.Component);
        Assert.Empty(attr.Properties);
    }

    [Fact]
    public void HintAttribute_WithComponent_StoresComponent()
    {
        var attr = new HintAttribute(PresentationHint.CodeEditor);

        Assert.Equal(PresentationHint.CodeEditor, attr.Component);
        Assert.Empty(attr.Properties);
    }

    [Fact]
    public void HintAttribute_WithProps_ParsesProperties()
    {
        var attr = new HintAttribute("language", ScriptLanguage.JavaScript, "theme", "dark");

        Assert.Null(attr.Component);
        Assert.Equal(ScriptLanguage.JavaScript, attr.Properties["language"]);
        Assert.Equal("dark", attr.Properties["theme"]);
    }

    [Fact]
    public void HintAttribute_WithComponentAndProps_ParsesProperties()
    {
        var attr = new HintAttribute(PresentationHint.Script, "language", ScriptLanguage.JavaScript);

        Assert.Equal(PresentationHint.Script, attr.Component);
        Assert.Equal(ScriptLanguage.JavaScript, attr.Properties["language"]);
    }

    [Fact]
    public void HintAttribute_PropsWithOddCount_IgnoresTrailingValue()
    {
        var attr = new HintAttribute("language", ScriptLanguage.JavaScript, "ignored");

        Assert.Single(attr.Properties);
        Assert.Equal(ScriptLanguage.JavaScript, attr.Properties["language"]);
    }

    [Fact]
    public void HintAttribute_PropsWithNonStringKey_SkipsEntry()
    {
        var attr = new HintAttribute(123, ScriptLanguage.JavaScript);

        Assert.Empty(attr.Properties);
    }

    [Fact]
    public void OptionsProviderAttribute_Stores_MethodName()
    {
        var attr = new OptionsProviderAttribute("GetOptions");

        Assert.Equal("GetOptions", attr.MethodName);
    }

    [Fact]
    public void OptionsProviderAttribute_NullMethodName_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new OptionsProviderAttribute(null!));
    }

    [Fact]
    public void DisplayConditionAttribute_AllowsMultiple()
    {
        var attributeUsage = typeof(DisplayConditionAttribute).GetCustomAttribute<AttributeUsageAttribute>();

        Assert.NotNull(attributeUsage);
        Assert.True(attributeUsage!.AllowMultiple);
    }
}

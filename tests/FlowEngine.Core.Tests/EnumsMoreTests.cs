using System.ComponentModel;
using System.Reflection;
using FlowEngine.Core.Enums;

namespace FlowEngine.Core.Tests;

public class EnumsMoreTests
{
    private static string GetDescription<T>(T value) where T : struct, Enum
    {
        var field = typeof(T).GetField(value.ToString());
        var attr = field?.GetCustomAttribute<DescriptionAttribute>();
        return attr?.Description ?? value.ToString();
    }

    [Theory]
    [InlineData(BackoffStrategy.Exponential, "指数退避")]
    [InlineData(BackoffStrategy.Linear, "线性退避")]
    [InlineData(BackoffStrategy.Fixed, "固定间隔")]
    public void BackoffStrategy_AllValues_HaveDescription(BackoffStrategy value, string expected)
    {
        Assert.Equal(expected, GetDescription(value));
    }

    [Theory]
    [InlineData(DraftStatus.Pending, "待审查")]
    [InlineData(DraftStatus.Rejected, "已拒绝")]
    [InlineData(DraftStatus.Confirmed, "已确认")]
    public void DraftStatus_AllValues_HaveDescription(DraftStatus value, string expected)
    {
        Assert.Equal(expected, GetDescription(value));
    }

    [Theory]
    [InlineData(ErrorStrategy.Terminate, "终止执行")]
    [InlineData(ErrorStrategy.Continue, "继续执行")]
    [InlineData(ErrorStrategy.Retry, "重试")]
    public void ErrorStrategy_AllValues_HaveDescription(ErrorStrategy value, string expected)
    {
        Assert.Equal(expected, GetDescription(value));
    }

    [Theory]
    [InlineData(ExecutionMode.OnceForAll, "对整个批次执行一次")]
    [InlineData(ExecutionMode.OncePerItem, "对每条数据项分别执行")]
    public void ExecutionMode_AllValues_HaveDescription(ExecutionMode value, string expected)
    {
        Assert.Equal(expected, GetDescription(value));
    }

    [Theory]
    [InlineData(HttpMethodOption.Get, "GET")]
    [InlineData(HttpMethodOption.Post, "POST")]
    [InlineData(HttpMethodOption.Put, "PUT")]
    [InlineData(HttpMethodOption.Delete, "DELETE")]
    [InlineData(HttpMethodOption.Patch, "PATCH")]
    public void HttpMethodOption_AllValues_HaveDescription(HttpMethodOption value, string expected)
    {
        Assert.Equal(expected, GetDescription(value));
    }

    [Theory]
    [InlineData(HttpRequestAuthMode.None, "None")]
    [InlineData(HttpRequestAuthMode.BearerToken, "Bearer Token")]
    [InlineData(HttpRequestAuthMode.ApiKey, "API Key")]
    [InlineData(HttpRequestAuthMode.BasicAuth, "Basic Auth")]
    [InlineData(HttpRequestAuthMode.QueryParameter, "Query Parameter")]
    public void HttpRequestAuthMode_AllValues_HaveDescription(HttpRequestAuthMode value, string expected)
    {
        Assert.Equal(expected, GetDescription(value));
    }

    [Theory]
    [InlineData(ParameterType.String, "字符串")]
    [InlineData(ParameterType.Number, "数字")]
    [InlineData(ParameterType.Boolean, "布尔值")]
    [InlineData(ParameterType.Options, "选项")]
    [InlineData(ParameterType.Json, "JSON")]
    [InlineData(ParameterType.Code, "代码")]
    [InlineData(ParameterType.Credential, "凭据")]
    [InlineData(ParameterType.Resource, "资源")]
    [InlineData(ParameterType.Array, "数组")]
    [InlineData(ParameterType.File, "文件")]
    [InlineData(ParameterType.Script, "脚本")]
    public void ParameterType_AllValues_HaveDescription(ParameterType value, string expected)
    {
        Assert.Equal(expected, GetDescription(value));
    }

    [Theory]
    [InlineData(PortDirection.Input, "输入")]
    [InlineData(PortDirection.Output, "输出")]
    public void PortDirection_AllValues_HaveDescription(PortDirection value, string expected)
    {
        Assert.Equal(expected, GetDescription(value));
    }

    [Theory]
    [InlineData(PortType.Main, "主数据端口")]
    [InlineData(PortType.AgentTool, "Agent 工具端口")]
    [InlineData(PortType.LLM, "LLM 供应端口")]
    [InlineData(PortType.Memory, "记忆端口")]
    public void PortType_AllValues_HaveDescription(PortType value, string expected)
    {
        Assert.Equal(expected, GetDescription(value));
    }

    [Theory]
    [InlineData(PresentationHint.Default, "默认")]
    [InlineData(PresentationHint.ButtonGroup, "按钮组")]
    [InlineData(PresentationHint.Select, "下拉选择")]
    [InlineData(PresentationHint.TextArea, "多行文本")]
    [InlineData(PresentationHint.CodeEditor, "代码编辑器")]
    [InlineData(PresentationHint.JsonEditor, "JSON 编辑器")]
    [InlineData(PresentationHint.KeyValueEditor, "键值对编辑器")]
    [InlineData(PresentationHint.Toggle, "开关")]
    [InlineData(PresentationHint.Secret, "密码输入")]
    [InlineData(PresentationHint.CredentialSelect, "凭据选择")]
    [InlineData(PresentationHint.ResourceSelect, "资源选择")]
    [InlineData(PresentationHint.FileUpload, "文件上传")]
    [InlineData(PresentationHint.Expression, "表达式")]
    [InlineData(PresentationHint.Script, "脚本")]
    [InlineData(PresentationHint.Array, "列表")]
    [InlineData(PresentationHint.DateTime, "日期时间")]
    public void PresentationHint_AllValues_HaveDescription(PresentationHint value, string expected)
    {
        Assert.Equal(expected, GetDescription(value));
    }

    [Theory]
    [InlineData(TriggerType.Schedule, "定时触发器")]
    [InlineData(TriggerType.Webhook, "Webhook 触发器")]
    [InlineData(TriggerType.Poll, "轮询触发器")]
    public void TriggerType_AllValues_HaveDescription(TriggerType value, string expected)
    {
        Assert.Equal(expected, GetDescription(value));
    }

    [Fact]
    public void TriggerType_Poll_HasExplicitValueTwo()
    {
        Assert.Equal(2, (int)TriggerType.Poll);
    }
}

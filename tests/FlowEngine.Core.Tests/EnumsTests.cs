using System.ComponentModel;
using System.Reflection;
using FlowEngine.Core.Enums;

namespace FlowEngine.Core.Tests;

/// <summary>
/// 枚举 [Description] 测试：验证 Core 各枚举的取值描述，
/// 并覆盖“无 [Description] 时回退到成员名”的兜底逻辑。
/// </summary>
public class EnumsTests
{
    /// <summary>
    /// 读取枚举值的 [Description]，缺失时回退为成员名。测试专用辅助，非生产代码。
    /// </summary>
    private static string GetDescription<T>(T value) where T : struct, Enum
    {
        var field = typeof(T).GetField(value.ToString());
        var attr = field?.GetCustomAttribute<DescriptionAttribute>();
        return attr?.Description ?? value.ToString();
    }

    [Fact]
    public void ExecutionStatus_Completed_Has_Expected_Description()
    {
        Assert.Equal("已完成", GetDescription(ExecutionStatus.Completed));
    }

    [Theory]
    [InlineData(ExecutionStatus.Pending, "待执行")]
    [InlineData(ExecutionStatus.Running, "执行中")]
    [InlineData(ExecutionStatus.Completed, "已完成")]
    [InlineData(ExecutionStatus.Failed, "失败")]
    [InlineData(ExecutionStatus.Cancelled, "已取消")]
    [InlineData(ExecutionStatus.Compensating, "补偿中")]
    [InlineData(ExecutionStatus.Compensated, "已补偿")]
    [InlineData(ExecutionStatus.CompensationFailed, "补偿失败")]
    [InlineData(ExecutionStatus.DryRunCompleted, "模拟运行完成")]
    public void ExecutionStatus_AllValues_Have_Description(ExecutionStatus status, string expected)
    {
        Assert.Equal(expected, GetDescription(status));
    }

    [Fact]
    public void WorkflowSource_Values_Have_Description_And_Explicit_Values()
    {
        Assert.Equal("人工创建", GetDescription(WorkflowSource.Human));
        Assert.Equal("AI 生成", GetDescription(WorkflowSource.Ai));
        Assert.Equal(0, (int)WorkflowSource.Human);
        Assert.Equal(1, (int)WorkflowSource.Ai);
    }

    public static IEnumerable<object[]> AllCoreEnumValues()
    {
        var enumTypes = typeof(ExecutionStatus).Assembly
            .GetTypes()
            .Where(t => t.IsEnum && t.Namespace == "FlowEngine.Core.Enums");

        foreach (var type in enumTypes)
        {
            foreach (var value in Enum.GetValues(type))
            {
                yield return [type, value!];
            }
        }
    }

    [Theory]
    [MemberData(nameof(AllCoreEnumValues))]
    public void AllCoreEnums_Have_NonEmpty_Description(Type enumType, object value)
    {
        var field = enumType.GetField(value.ToString()!);
        var attr = field?.GetCustomAttribute<DescriptionAttribute>();

        Assert.NotNull(attr);
        Assert.False(string.IsNullOrWhiteSpace(attr!.Description));
    }

    [Fact]
    public void GetDescription_FallsBackToMemberName_WhenNoAttribute()
    {
        Assert.Equal(nameof(Undescribed.Color), GetDescription(Undescribed.Color));
    }

    /// <summary>
    /// 测试专用枚举：故意不带 [Description]，用于验证兜底分支。
    /// </summary>
    private enum Undescribed
    {
        Color
    }
}

using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;

namespace FlowEngine.Core.Tests;

public class EntitiesTests
{
    [Fact]
    public void NodeExecutionResult_Defaults_To_Empty_Output()
    {
        var result = new NodeExecutionResult();

        Assert.NotNull(result.Output);
        Assert.Empty(result.Output.Items);
    }

    [Fact]
    public void NodeExecutionResult_BranchIndex_RoundTrips()
    {
        var result = new NodeExecutionResult();

        result.BranchIndex = 2;

        Assert.Equal(2, result.BranchIndex);
    }

    [Fact]
    public void DataBatch_Can_Hold_Items()
    {
        var batch = new DataBatch
        {
            Items =
            [
                new DataItem { Success = true },
                new DataItem { Success = false }
            ]
        };

        Assert.Equal(2, batch.Items.Count);
    }

    [Fact]
    public void NodeDefinition_Supports_Entry_Flag_And_Error_Strategy()
    {
        var node = new NodeDefinition
        {
            Id = "node-1",
            TypeName = "httpRequest",
            IsEntry = true,
            ErrorStrategy = ErrorStrategy.Continue
        };

        Assert.True(node.IsEntry);
        Assert.Equal(ErrorStrategy.Continue, node.ErrorStrategy);
    }
}

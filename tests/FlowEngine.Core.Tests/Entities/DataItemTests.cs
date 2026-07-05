using System.Text.Json;
using System.Text.Json.Nodes;
using FlowEngine.Core.Entities;

namespace FlowEngine.Core.Tests.Entities;

/// <summary>
/// DataItem 单元测试。
/// </summary>
public class DataItemTests
{
    [Fact]
    public void AttachmentId_RoundTrips_NonNull()
    {
        // Arrange
        var attachmentId = Guid.NewGuid();
        var item = new DataItem
        {
            Data = JsonValue.Create("payload"),
            Success = true,
            AttachmentId = attachmentId
        };

        // Act
        var json = JsonSerializer.Serialize(item);
        var roundTripped = JsonSerializer.Deserialize<DataItem>(json);

        // Assert
        Assert.NotNull(roundTripped);
        Assert.Equal(attachmentId, roundTripped.AttachmentId);
    }

    [Fact]
    public void AttachmentId_RoundTrips_Null()
    {
        // Arrange
        var item = new DataItem
        {
            Data = JsonValue.Create("payload"),
            Success = true,
            AttachmentId = null
        };

        // Act
        var json = JsonSerializer.Serialize(item);
        var roundTripped = JsonSerializer.Deserialize<DataItem>(json);

        // Assert
        Assert.NotNull(roundTripped);
        Assert.Null(roundTripped.AttachmentId);
    }
}

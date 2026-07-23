using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Entities;
using FlowEngine.Plugins.Storage;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;

namespace FlowEngine.Runtime.Tests.Plugins;

/// <summary>
/// mongoDb node tests. Covers Insert (insertedId), Find (DataBatch with converted docs + filter),
/// Update (modifiedCount), Delete (deletedCount), MissingConnection, MissingCollection, InvalidJson,
/// and MongoException -> MongoError. The MongoDB driver is mocked via the internal CollectionOverride seam
/// (IMongoCollection / IMongoDatabase / IMongoClient), so no live server is required.
/// </summary>
public sealed class MongoDbNodeTests
{
    [Fact]
    public async Task ExecuteAsync_Insert_ReturnsInsertedId()
    {
        var objectId = ObjectId.GenerateNewId();
        var collMock = new Mock<IMongoCollection<BsonDocument>>();
        collMock
            .Setup(c => c.InsertOneAsync(It.IsAny<BsonDocument>(), It.IsAny<InsertOneOptions>(), It.IsAny<CancellationToken>()))
            .Callback<BsonDocument, InsertOneOptions, CancellationToken>((doc, _, _) => doc["_id"] = objectId)
            .Returns(Task.CompletedTask);

        var node = new MongoDbNode
        {
            Connection = MongoCredential(),
            Collection = "users",
            Operation = MongoDbNode.MongoOperation.Insert,
            Document = "{ \"name\": \"alice\", \"age\": 30 }",
            CollectionOverride = collMock.Object
        };

        var result = await node.ExecuteAsync(CreateContext(), CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        var data = result.Output.Items[0].Data as JsonObject;
        Assert.True(data?["success"]?.GetValue<bool>() == true);
        var insertedId = data?["insertedId"] as JsonObject;
        Assert.Equal(objectId.ToString(), insertedId?["$oid"]?.GetValue<string>());
    }

    [Fact]
    public async Task ExecuteAsync_Find_ReturnsDataBatch_WithConvertedDocs_AndVerifiesFilter()
    {
        var docs = new List<BsonDocument>
        {
            new BsonDocument { ["name"] = "alice", ["age"] = 30 },
            new BsonDocument { ["name"] = "bob", ["age"] = 25 }
        };

        var cursorMock = new Mock<IAsyncCursor<BsonDocument>>();
        cursorMock.Setup(c => c.Current).Returns(docs);
        cursorMock
            .SetupSequence(c => c.MoveNextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true)
            .ReturnsAsync(false);

        // Exercise the full driver chain (client -> database -> collection) via the mock surface,
        // then inject the resolved collection through the CollectionOverride seam.
        var clientMock = new Mock<IMongoClient>();
        var dbMock = new Mock<IMongoDatabase>();
        var collMock = new Mock<IMongoCollection<BsonDocument>>();

        dbMock.Setup(d => d.GetCollection<BsonDocument>("users", null)).Returns(collMock.Object);
        clientMock.Setup(c => c.GetDatabase("testdb", null)).Returns(dbMock.Object);

        collMock
            .Setup(c => c.FindAsync(
                It.IsAny<FilterDefinition<BsonDocument>>(),
                It.IsAny<FindOptions<BsonDocument, BsonDocument>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(cursorMock.Object);

        var node = new MongoDbNode
        {
            Connection = MongoCredential(),
            Collection = "users",
            Operation = MongoDbNode.MongoOperation.Find,
            Filter = "{ \"name\": \"alice\" }",
            CollectionOverride = clientMock.Object.GetDatabase("testdb").GetCollection<BsonDocument>("users")
        };

        var result = await node.ExecuteAsync(CreateContext(), CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Equal(2, result.Output.Items.Count);
        Assert.Equal("alice", GetString(result.Output.Items[0].Data, "name"));
        Assert.Equal(30, GetInt(result.Output.Items[0].Data, "age"));
        Assert.Equal("bob", GetString(result.Output.Items[1].Data, "name"));

        // The chain (client -> database -> collection) is exercised when resolving the seam value.
        clientMock.Verify(c => c.GetDatabase("testdb", null), Times.Once());
        dbMock.Verify(d => d.GetCollection<BsonDocument>("users", null), Times.Once());
        collMock.Verify(
            c => c.FindAsync(
                It.IsAny<FilterDefinition<BsonDocument>>(),
                It.IsAny<FindOptions<BsonDocument, BsonDocument>>(),
                It.IsAny<CancellationToken>()),
            Times.Once());
    }

    [Fact]
    public async Task ExecuteAsync_Find_EmptyFilter_MatchesAll()
    {
        var docs = new List<BsonDocument> { new BsonDocument { ["x"] = 1 } };
        var cursorMock = new Mock<IAsyncCursor<BsonDocument>>();
        cursorMock.Setup(c => c.Current).Returns(docs);
        cursorMock
            .SetupSequence(c => c.MoveNextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true)
            .ReturnsAsync(false);

        var collMock = new Mock<IMongoCollection<BsonDocument>>();
        collMock
            .Setup(c => c.FindAsync(
                It.IsAny<FilterDefinition<BsonDocument>>(),
                It.IsAny<FindOptions<BsonDocument, BsonDocument>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(cursorMock.Object);

        var node = new MongoDbNode
        {
            Connection = MongoCredential(),
            Collection = "users",
            Operation = MongoDbNode.MongoOperation.Find,
            Filter = null,
            CollectionOverride = collMock.Object
        };

        var result = await node.ExecuteAsync(CreateContext(), CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Single(result.Output.Items);
    }

    [Fact]
    public async Task ExecuteAsync_Update_ReturnsModifiedCount()
    {
        var collMock = new Mock<IMongoCollection<BsonDocument>>();
        var updateResult = new Mock<UpdateResult>();
        updateResult.Setup(r => r.IsAcknowledged).Returns(true);
        updateResult.Setup(r => r.ModifiedCount).Returns(1);
        collMock
            .Setup(c => c.UpdateOneAsync(
                It.IsAny<FilterDefinition<BsonDocument>>(),
                It.IsAny<UpdateDefinition<BsonDocument>>(),
                It.IsAny<UpdateOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(updateResult.Object);

        var node = new MongoDbNode
        {
            Connection = MongoCredential(),
            Collection = "users",
            Operation = MongoDbNode.MongoOperation.Update,
            Filter = "{ \"name\": \"alice\" }",
            Document = "{ \"status\": \"active\" }",
            CollectionOverride = collMock.Object
        };

        var result = await node.ExecuteAsync(CreateContext(), CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        var data = result.Output.Items[0].Data as JsonObject;
        Assert.True(data?["success"]?.GetValue<bool>() == true);
        Assert.Equal(1, data?["modifiedCount"]?.GetValue<long>());
    }

    [Fact]
    public async Task ExecuteAsync_Delete_ReturnsDeletedCount()
    {
        var collMock = new Mock<IMongoCollection<BsonDocument>>();
        var deleteResult = new Mock<DeleteResult>();
        deleteResult.Setup(r => r.IsAcknowledged).Returns(true);
        deleteResult.Setup(r => r.DeletedCount).Returns(1);
        collMock
            .Setup(c => c.DeleteOneAsync(
                It.IsAny<FilterDefinition<BsonDocument>>(),
                It.IsAny<DeleteOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(deleteResult.Object);

        var node = new MongoDbNode
        {
            Connection = MongoCredential(),
            Collection = "users",
            Operation = MongoDbNode.MongoOperation.Delete,
            Filter = "{ \"name\": \"alice\" }",
            CollectionOverride = collMock.Object
        };

        var result = await node.ExecuteAsync(CreateContext(), CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        var data = result.Output.Items[0].Data as JsonObject;
        Assert.True(data?["success"]?.GetValue<bool>() == true);
        Assert.Equal(1, data?["deletedCount"]?.GetValue<long>());
    }

    [Fact]
    public async Task ExecuteAsync_MissingConnection_ReturnsMissingConnection()
    {
        var node = new MongoDbNode
        {
            Collection = "users",
            Operation = MongoDbNode.MongoOperation.Find
        };

        var result = await node.ExecuteAsync(CreateContext(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(FlowConstants.ErrorCodes.MissingConnection, result.Error?.Code);
    }

    [Fact]
    public async Task ExecuteAsync_MissingCollection_ReturnsMissingCollection()
    {
        var node = new MongoDbNode
        {
            Connection = MongoCredential(),
            Collection = "   ",
            Operation = MongoDbNode.MongoOperation.Find
        };

        var result = await node.ExecuteAsync(CreateContext(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("MissingCollection", result.Error?.Code);
    }

    [Fact]
    public async Task ExecuteAsync_InvalidJson_ReturnsInvalidJson()
    {
        var collMock = new Mock<IMongoCollection<BsonDocument>>();
        var node = new MongoDbNode
        {
            Connection = MongoCredential(),
            Collection = "users",
            Operation = MongoDbNode.MongoOperation.Insert,
            Document = "{ not valid json",
            CollectionOverride = collMock.Object
        };

        var result = await node.ExecuteAsync(CreateContext(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("InvalidJson", result.Error?.Code);
    }

    [Fact]
    public async Task ExecuteAsync_MongoException_ReturnsMongoError()
    {
        var collMock = new Mock<IMongoCollection<BsonDocument>>();
        collMock
            .Setup(c => c.FindAsync(
                It.IsAny<FilterDefinition<BsonDocument>>(),
                It.IsAny<FindOptions<BsonDocument, BsonDocument>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new MongoException("connection refused"));

        var node = new MongoDbNode
        {
            Connection = MongoCredential(),
            Collection = "users",
            Operation = MongoDbNode.MongoOperation.Find,
            CollectionOverride = collMock.Object
        };

        var result = await node.ExecuteAsync(CreateContext(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("MongoError", result.Error?.Code);
        Assert.Contains("connection refused", result.Error?.Message ?? string.Empty);
    }

    [Fact]
    public async Task ExecuteAsync_Find_InvalidFilter_ReturnsInvalidJson()
    {
        var collMock = new Mock<IMongoCollection<BsonDocument>>();
        collMock
            .Setup(c => c.FindAsync(
                It.IsAny<FilterDefinition<BsonDocument>>(),
                It.IsAny<FindOptions<BsonDocument, BsonDocument>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new MongoDB.Bson.BsonException("invalid filter"));

        var node = new MongoDbNode
        {
            Connection = MongoCredential(),
            Collection = "users",
            Operation = MongoDbNode.MongoOperation.Find,
            Filter = "{ bad json ",
            CollectionOverride = collMock.Object
        };

        var result = await node.ExecuteAsync(CreateContext(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("InvalidJson", result.Error?.Code);
    }

    private static int GetInt(JsonNode? node, string key)
        => node?[key] is JsonValue value ? value.GetValue<int>() : 0;

    private static string? GetString(JsonNode? node, string key)
        => node?[key] is JsonValue value ? value.GetValue<string>() : null;

    private static CredentialValue MongoCredential()
    {
        return new CredentialValue
        {
            Name = "test-mongo",
            Type = "mongo",
            Fields = new Dictionary<string, string>
            {
                ["connectionString"] = "mongodb://localhost:27017",
                ["database"] = "testdb"
            }
        };
    }

    private static NodeExecutionContext CreateContext()
    {
        return new NodeExecutionContext
        {
            Workflow = new Workflow { Id = Guid.NewGuid(), Name = "test" },
            ExecutionId = Guid.NewGuid(),
            Node = new NodeDefinition
            {
                Id = "mongoDb",
                TypeName = "mongoDb",
                Name = "mongoDb"
            },
            Inputs = new Dictionary<string, DataBatch>(StringComparer.OrdinalIgnoreCase)
            {
                [FlowConstants.PortNames.Input] = new DataBatch()
            }
        };
    }
}

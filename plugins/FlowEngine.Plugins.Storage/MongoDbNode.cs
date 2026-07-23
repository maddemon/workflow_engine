using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Attributes;
using FlowEngine.Core.Ai;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Exceptions;
using MongoDB.Bson;
using MongoDB.Driver;

namespace FlowEngine.Plugins.Storage;

/// <summary>
/// MongoDB node. Executes a single storage operation (Insert / Find / Update / Delete)
/// against a MongoDB collection using the <c>mongo</c> credential type.
/// The node references only <see cref="FlowEngine.Core"/> plus the MongoDB driver.
/// </summary>
public sealed class MongoDbNode : INodeType
{
    /// <summary>
    /// MongoDB operation selector.
    /// </summary>
    public enum MongoOperation
    {
        /// <summary>Insert a single document.</summary>
        Insert,

        /// <summary>Find documents matching the filter. Default.</summary>
        Find,

        /// <summary>Update the first document matching the filter.</summary>
        Update,

        /// <summary>Delete the first document matching the filter.</summary>
        Delete
    }

    /// <inheritdoc />
    public string TypeName => "mongoDb";

    /// <inheritdoc />
    public string DisplayName => "MongoDB";

    /// <inheritdoc />
    public string Category => "Storage";

    /// <inheritdoc />
    public string Icon => "mongo";

    /// <inheritdoc />
    public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;

    /// <inheritdoc />
    public IReadOnlyList<PortDefinition> Ports { get; } =
    [
        new PortDefinition { Name = FlowConstants.PortNames.Input, DisplayName = "Input", Direction = PortDirection.Input, Type = PortType.Main },
        new PortDefinition { Name = FlowConstants.PortNames.Output, DisplayName = "Output", Direction = PortDirection.Output, Type = PortType.Main }
    ];

    /// <inheritdoc />
    public bool DefaultIsEntry => false;

    /// <summary>
    /// MongoDB connection credential (type <c>mongo</c>). Fields: connectionString (secret), database.
    /// The connection string is a secret and is never written to logs or exceptions.
    /// </summary>
    [Credential("mongo")]
    [Description("MongoDB connection credential (type: mongo). Fields: connectionString (secret), database.")]
    public CredentialValue? Connection { get; set; }

    /// <summary>
    /// Target collection name. Required.
    /// </summary>
    [Description("Target collection name. Required.")]
    public string? Collection { get; set; }

    /// <summary>
    /// Storage operation to perform. Defaults to <see cref="MongoOperation.Find"/>.
    /// </summary>
    [Description("Storage operation to perform. Defaults to Find.")]
    public MongoOperation Operation { get; set; } = MongoOperation.Find;

    /// <summary>
    /// Optional MongoDB filter expressed as a JSON document (e.g. <c>{ "name": "alice" }</c>).
    /// Empty/null matches all documents.
    /// </summary>
    [Description("Optional MongoDB filter as JSON (e.g. {\"name\":\"alice\"}). Empty/null matches all.")]
    public string? Filter { get; set; }

    /// <summary>
    /// Document JSON for Insert, or update document JSON for Update.
    /// For Update, a plain document is wrapped in <c>$set</c>; a document whose first-level keys
    /// already start with <c>$</c> (e.g. <c>$inc</c>) is used as-is.
    /// </summary>
    [Description("Document JSON for Insert, or update document JSON for Update.")]
    public string? Document { get; set; }

    /// <summary>
    /// Internal testable seam. When set, the node uses this collection instead of building one
    /// from the credential at runtime, so the MongoDB driver interfaces can be mocked in tests.
    /// </summary>
    internal IMongoCollection<BsonDocument>? CollectionOverride { get; set; }

    /// <inheritdoc />
    public async Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            if (Connection is null)
            {
                return context.ErrorResult(FlowConstants.ErrorCodes.MissingConnection, "MongoDB connection credential is required.");
            }

            if (!Connection.Fields.TryGetValue("connectionString", out var connectionString) || string.IsNullOrWhiteSpace(connectionString))
            {
                return context.ErrorResult(FlowConstants.ErrorCodes.MissingConnection, "MongoDB connectionString is required.");
            }

            if (!Connection.Fields.TryGetValue("database", out var database) || string.IsNullOrWhiteSpace(database))
            {
                return context.ErrorResult(FlowConstants.ErrorCodes.MissingConnection, "MongoDB database is required.");
            }

            if (string.IsNullOrWhiteSpace(Collection))
            {
                return context.ErrorResult("MissingCollection", "Collection name is required.");
            }

            var collection = CollectionOverride
                ?? new MongoClient(connectionString).GetDatabase(database).GetCollection<BsonDocument>(Collection);

            var filter = BuildFilter(Filter);

            switch (Operation)
            {
                case MongoOperation.Insert:
                    return await ExecuteInsertAsync(context, collection, cancellationToken).ConfigureAwait(false);
                case MongoOperation.Find:
                    return await ExecuteFindAsync(context, collection, filter, cancellationToken).ConfigureAwait(false);
                case MongoOperation.Update:
                    return await ExecuteUpdateAsync(context, collection, filter, cancellationToken).ConfigureAwait(false);
                case MongoOperation.Delete:
                    return await ExecuteDeleteAsync(context, collection, filter, cancellationToken).ConfigureAwait(false);
                default:
                    return context.ErrorResult(FlowConstants.ErrorCodes.UnexpectedError, $"Unsupported operation: {Operation}");
            }
        }
        catch (MongoException ex)
        {
            // Only non-sensitive info is logged; connectionString is never written.
            context.Logger?.LogError(ex, "mongoDb 操作失败（集合 {Collection}，操作 {Operation}）。", Collection, Operation);
            return context.ErrorResult("MongoError", $"MongoDB error: {ex.Message}");
        }
        catch (BsonException ex)
        {
            return context.ErrorResult("InvalidJson", $"Invalid MongoDB JSON: {ex.Message}");
        }
        catch (JsonException ex)
        {
            return context.ErrorResult("InvalidJson", $"Invalid MongoDB JSON: {ex.Message}");
        }
        catch (Exception ex)
        {
            return context.ErrorResult(FlowConstants.ErrorCodes.UnexpectedError, $"Unexpected MongoDB error: {ex.Message}");
        }
    }

    private async Task<NodeExecutionResult> ExecuteInsertAsync(
        NodeExecutionContext context, IMongoCollection<BsonDocument> collection, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Document))
        {
            return context.ErrorResult("InvalidJson", "Document is required for Insert.");
        }

        BsonDocument doc;
        try
        {
            doc = BsonDocument.Parse(Document!);
        }
        catch (Exception ex)
        {
            return context.ErrorResult("InvalidJson", $"Document is not valid JSON: {ex.Message}");
        }

        await collection.InsertOneAsync(doc, (InsertOneOptions?)null, cancellationToken).ConfigureAwait(false);

        var insertedId = doc["_id"] is { } id ? JsonNode.Parse(id.ToJson()) : null;

        context.Logger?.LogInformation("mongoDb Insert 完成：集合 {Collection}，新增 1 条。", Collection);

        return context.Ok(new JsonObject
        {
            ["success"] = true,
            ["insertedId"] = insertedId
        });
    }

    private async Task<NodeExecutionResult> ExecuteFindAsync(
        NodeExecutionContext context, IMongoCollection<BsonDocument> collection,
        FilterDefinition<BsonDocument> filter, CancellationToken cancellationToken)
    {
        using var cursor = await collection.FindAsync(filter, null, cancellationToken).ConfigureAwait(false);

        var docs = await cursor.ToListAsync(cancellationToken).ConfigureAwait(false);

        var batch = new DataBatch();
        for (var i = 0; i < docs.Count; i++)
        {
            batch.Items.Add(new DataItem
            {
                Data = JsonNode.Parse(docs[i].ToJson()) as JsonObject,
                Success = true,
                SourceIndex = i
            });
        }

        context.Logger?.LogInformation("mongoDb Find 完成：集合 {Collection}，返回 {Count} 条。", Collection, docs.Count);

        return context.Ok(batch);
    }

    private async Task<NodeExecutionResult> ExecuteUpdateAsync(
        NodeExecutionContext context, IMongoCollection<BsonDocument> collection,
        FilterDefinition<BsonDocument> filter, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Document))
        {
            return context.ErrorResult("InvalidJson", "Document is required for Update.");
        }

        BsonDocument updateDoc;
        try
        {
            updateDoc = BsonDocument.Parse(Document!);
        }
        catch (Exception ex)
        {
            return context.ErrorResult("InvalidJson", $"Document is not valid JSON: {ex.Message}");
        }

        var update = new BsonDocumentUpdateDefinition<BsonDocument>(WrapUpdate(updateDoc));

        var result = await collection.UpdateOneAsync(filter, update, null, cancellationToken).ConfigureAwait(false);

        var modifiedCount = result.ModifiedCount;

        context.Logger?.LogInformation("mongoDb Update 完成：集合 {Collection}，修改 {Count} 条。", Collection, modifiedCount);

        return context.Ok(new JsonObject
        {
            ["success"] = true,
            ["modifiedCount"] = (long)modifiedCount
        });
    }

    private async Task<NodeExecutionResult> ExecuteDeleteAsync(
        NodeExecutionContext context, IMongoCollection<BsonDocument> collection,
        FilterDefinition<BsonDocument> filter, CancellationToken cancellationToken)
    {
        var result = await collection.DeleteOneAsync(filter, null, cancellationToken).ConfigureAwait(false);

        var deletedCount = result.DeletedCount;

        context.Logger?.LogInformation("mongoDb Delete 完成：集合 {Collection}，删除 {Count} 条。", Collection, deletedCount);

        return context.Ok(new JsonObject
        {
            ["success"] = true,
            ["deletedCount"] = (long)deletedCount
        });
    }

    private static FilterDefinition<BsonDocument> BuildFilter(string? filterJson)
    {
        if (string.IsNullOrWhiteSpace(filterJson))
        {
            return FilterDefinition<BsonDocument>.Empty;
        }

        return new JsonFilterDefinition<BsonDocument>(filterJson!);
    }

    /// <summary>
    /// Wraps a plain document in <c>$set</c> so UpdateOne treats it as a field update.
    /// Documents whose first-level keys already start with <c>$</c> (operators like <c>$inc</c>)
    /// are returned unchanged.
    /// </summary>
    private static BsonDocument WrapUpdate(BsonDocument doc)
    {
        foreach (var element in doc.Elements)
        {
            if (element.Name.StartsWith("$", StringComparison.Ordinal))
            {
                return doc;
            }
        }

        return new BsonDocument("$set", doc);
    }

    /// <inheritdoc />
    AiNodeDefinition? INodeType.GetAiDefinition(NodeTypeDescriptor descriptor) =>
        AiDefinitionHelpers.Def(
            "MongoDB", "Storage", false,
            "MongoDB 节点：对集合执行 Insert/Find/Update/Delete 操作，凭据类型为 mongo。",
            ["mongodb", "storage", "database", "nosql"],
            null,
            AiDefinitionHelpers.Example("查找",
                JsonNode.Parse("""{"Collection":"users","Operation":"Find","Filter":"{\"name\":\"alice\"}"}"""),
                JsonNode.Parse("""{"success":true}""")));
}

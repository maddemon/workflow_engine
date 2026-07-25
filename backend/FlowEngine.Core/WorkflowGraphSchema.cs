using System.Text.Json.Nodes;

namespace FlowEngine.Core;

/// <summary>
/// 工作流图（Nodes / Connections JSON 列）schema 版本与迁移钩子（EXT-5）。
///
/// 设计目标：在不破坏现有数据可读性的前提下，为节点/连接 JSON 提供前向兼容的演进能力。
/// 旧数据（裸数组、无版本标记）一律按 <see cref="CurrentVersion"/>（即 v1）处理并通过，无需迁移；
/// 未来新增字段/结构调整时，只需 <see cref="RegisterMigration"/> 注册一个 `fromVersion → fromVersion+1`
/// 的迁移函数，归一化阶段便会按版本顺序应用，直至 <see cref="CurrentVersion"/>。
///
/// 采用说明：本工具为"可用"的归一化/迁移缝。将其实际接入持久化边界（如为 Workflow 增加
/// graphSchemaVersion 列、在装配/导入时调用 <see cref="NormalizeGraph"/>）为后续低风险步骤，
/// 当前未强制改动 DB 列格式，确保存量数据零风险可读。
/// </summary>
public static class WorkflowGraphSchema
{
    /// <summary>
    /// 当前图 schema 版本。
    /// </summary>
    public const int CurrentVersion = 1;

    /// <summary>
    /// 单个版本的迁移委托：输入该版本的图 JSON，返回迁移后的图 JSON。
    /// </summary>
    /// <param name="graph">待迁移的节点/连接 JSON（数组）。</param>
    public delegate JsonNode GraphMigration(JsonNode graph);

    private static readonly Dictionary<int, GraphMigration> Migrations = new();
    private static readonly object SyncRoot = new();

    /// <summary>
    /// 注册从 <paramref name="fromVersion"/> 到 <c>fromVersion + 1</c> 的迁移。
    /// 应在应用启动时、读取数据前注册。
    /// </summary>
    public static void RegisterMigration(int fromVersion, GraphMigration migration)
    {
        ArgumentNullException.ThrowIfNull(migration);
        if (fromVersion < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(fromVersion), "迁移起始版本必须大于等于 1。");
        }

        lock (SyncRoot)
        {
            Migrations[fromVersion] = migration;
        }
    }

    /// <summary>
    /// 读取图版本。支持信封对象 <c>{ schemaVersion, nodes, connections }</c>；
    /// 旧格式（裸数组或无 schemaVersion 字段）按 <see cref="CurrentVersion"/> 处理。
    /// </summary>
    public static int ReadVersion(JsonNode graph)
    {
        if (graph is JsonObject obj &&
            obj.TryGetPropertyValue("schemaVersion", out var versionNode) &&
            versionNode is JsonValue value &&
            value.TryGetValue<int>(out var version))
        {
            return version;
        }

        return CurrentVersion;
    }

    /// <summary>
    /// 归一化单个图数组（nodes 或 connections）。
    /// 从 <paramref name="sourceVersion"/> 起，依次应用已注册的迁移，直至 <paramref name="targetVersion"/>。
    /// 旧数据（targetVersion == sourceVersion 且无对应迁移）原样返回，保证存量可读。
    /// </summary>
    /// <param name="array">节点或连接 JSON 数组。</param>
    /// <param name="sourceVersion">源版本；缺省按当前版本（即不迁移）。</param>
    /// <param name="targetVersion">目标版本；缺省为 <see cref="CurrentVersion"/>。未来版本演进时显式传入更高值即可触发迁移。</param>
    public static JsonNode NormalizeArray(JsonNode array, int sourceVersion = CurrentVersion, int targetVersion = CurrentVersion)
    {
        ArgumentNullException.ThrowIfNull(array);
        if (targetVersion < sourceVersion)
        {
            throw new ArgumentOutOfRangeException(nameof(targetVersion), "目标版本不得低于源版本。");
        }

        var current = array.DeepClone();
        for (var version = sourceVersion; version < targetVersion; version++)
        {
            GraphMigration? migration = null;
            lock (SyncRoot)
            {
                Migrations.TryGetValue(version, out migration);
            }

            if (migration is not null)
            {
                current = migration(current);
            }
        }

        return current;
    }

    /// <summary>
    /// 归一化整图信封 <c>{ schemaVersion, nodes, connections }</c>。
    /// 读取版本后分别对 nodes / connections 执行迁移，并回写 <paramref name="targetVersion"/>。
    /// 传入裸数组时按 nodes 处理（v1，不迁移），并包装为信封返回，便于兼容旧格式且补齐版本标记。
    /// </summary>
    /// <param name="targetVersion">目标版本；缺省为 <see cref="CurrentVersion"/>。</param>
    public static JsonNode NormalizeGraph(JsonNode graph, int targetVersion = CurrentVersion)
    {
        ArgumentNullException.ThrowIfNull(graph);

        if (graph is not JsonObject envelope)
        {
            // 旧格式裸数组：按 nodes 处理，包装为信封并补齐版本标记，保持可读。
            var bareNodes = NormalizeArray(graph, ReadVersion(graph), targetVersion);
            return new JsonObject
            {
                ["schemaVersion"] = targetVersion,
                ["nodes"] = bareNodes,
                ["connections"] = new JsonArray(),
            };
        }

        var sourceVersion = ReadVersion(envelope);
        var nodes = envelope["nodes"] is JsonNode n ? NormalizeArray(n, sourceVersion, targetVersion) : new JsonArray();
        var connections = envelope["connections"] is JsonNode c ? NormalizeArray(c, sourceVersion, targetVersion) : new JsonArray();

        var result = new JsonObject
        {
            ["schemaVersion"] = targetVersion,
            ["nodes"] = nodes,
            ["connections"] = connections,
        };
        return result;
    }
}

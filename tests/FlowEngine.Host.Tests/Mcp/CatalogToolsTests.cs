using System.ComponentModel;
using System.Reflection;
using FlowEngine.Application.Workflows;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Ai;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Host.Mcp;
using FlowEngine.Host.Mcp.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Moq;

namespace FlowEngine.Host.Tests.Mcp;

/// <summary>
/// Catalog MCP 工具单元测试。
/// </summary>
public class CatalogToolsTests
{
    /// <summary>
    /// list_node_catalog 在未传入 category 时应返回全部节点摘要。
    /// </summary>
    [Fact]
    public void ListNodeCatalog_WithoutCategory_ReturnsAllSummaries()
    {
        var catalogService = CreateCatalogService([
            CreateDescriptor("coreNode", "Core"),
            CreateDescriptor("integrationNode", "Integration"),
        ]);
        var tools = new CatalogTools(catalogService);

        var result = tools.ListNodeCatalog();

        Assert.Equal(2, result.Count);
        Assert.Contains(result, n => n.Name == "coreNode");
        Assert.Contains(result, n => n.Name == "integrationNode");
    }

    /// <summary>
    /// list_node_catalog 应按 category 大小写不敏感过滤。
    /// </summary>
    [Theory]
    [InlineData("core")]
    [InlineData("CORE")]
    [InlineData("Core")]
    public void ListNodeCatalog_WithCategoryFilter_CaseInsensitive_ReturnsMatching(string category)
    {
        var catalogService = CreateCatalogService([
            CreateDescriptor("coreNode", "Core"),
            CreateDescriptor("triggerNode", "Trigger"),
            CreateDescriptor("integrationNode", "Integration"),
        ]);
        var tools = new CatalogTools(catalogService);

        var result = tools.ListNodeCatalog(category);

        Assert.Single(result);
        Assert.Equal("coreNode", result[0].Name);
        Assert.Equal("Core", result[0].Category);
    }

    /// <summary>
    /// list_node_catalog 在不存在匹配分类时返回空列表。
    /// </summary>
    [Fact]
    public void ListNodeCatalog_WithCategoryFilter_NoMatches_ReturnsEmpty()
    {
        var catalogService = CreateCatalogService([CreateDescriptor("coreNode", "Core")]);
        var tools = new CatalogTools(catalogService);

        var result = tools.ListNodeCatalog("missing");

        Assert.Empty(result);
    }

    /// <summary>
    /// get_node_detail 在节点存在时返回完整定义。
    /// </summary>
    [Fact]
    public void GetNodeDetail_ExistingNode_ReturnsDefinition()
    {
        var catalogService = CreateCatalogService([CreateDescriptor("coreNode", "Core")]);
        var tools = new CatalogTools(catalogService);

        var result = tools.GetNodeDetail("coreNode");

        var definition = Assert.IsType<AiNodeDefinition>(result);
        Assert.Equal("coreNode", definition.Name);
        Assert.Equal("Core", definition.Category);
        Assert.NotNull(definition.InputSchema);
    }

    /// <summary>
    /// get_node_detail 在节点不存在时返回结构化错误，不抛异常。
    /// </summary>
    [Fact]
    public void GetNodeDetail_NonExistingNode_ReturnsStructuredError()
    {
        var catalogService = CreateCatalogService([CreateDescriptor("coreNode", "Core")]);
        var tools = new CatalogTools(catalogService);

        var result = tools.GetNodeDetail("unknownNode");

        var error = Assert.IsType<McpToolError>(result);
        Assert.False(error.Success);
        Assert.Equal("NodeNotFound", error.ErrorCode);
        Assert.Contains("unknownNode", error.Message);
        Assert.False(error.CanAutoFix);
        Assert.Equal("请先用 list_node_catalog 查看可用节点", error.SuggestedFix);
    }

    /// <summary>
    /// get_node_detail 在名称为 null、空字符串或纯空白时返回 InvalidInput 结构化错误，不抛异常。
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GetNodeDetail_NullOrWhiteSpaceName_ReturnsInvalidInputError(string? name)
    {
        var catalogService = CreateCatalogService([]);
        var tools = new CatalogTools(catalogService);

        var result = tools.GetNodeDetail(name!);

        var error = Assert.IsType<McpToolError>(result);
        Assert.False(error.Success);
        Assert.Equal("InvalidInput", error.ErrorCode);
        Assert.Equal("节点名称不能为空", error.Message);
        Assert.True(error.CanAutoFix);
        Assert.Equal("请提供非空的节点类型名", error.SuggestedFix);
    }

    /// <summary>
    /// CatalogTools 应被 MCP SDK 的工具扫描机制识别，并注册指定名称的工具。
    /// </summary>
    [Theory]
    [InlineData(nameof(CatalogTools.ListNodeCatalog), "list_node_catalog")]
    [InlineData(nameof(CatalogTools.GetNodeDetail), "get_node_detail")]
    public void ToolMethods_AreDiscoveredWithExpectedNames(string methodName, string expectedToolName)
    {
        var typeInfo = typeof(CatalogTools);
        Assert.NotNull(typeInfo.GetCustomAttribute<McpServerToolTypeAttribute>());

        var method = typeInfo.GetMethod(methodName);
        Assert.NotNull(method);

        var toolAttribute = method.GetCustomAttribute<McpServerToolAttribute>();
        Assert.NotNull(toolAttribute);
        Assert.Equal(expectedToolName, toolAttribute!.Name);

        var descriptionAttribute = method.GetCustomAttribute<DescriptionAttribute>();
        Assert.NotNull(descriptionAttribute);
        Assert.False(string.IsNullOrWhiteSpace(descriptionAttribute!.Description));
    }

    /// <summary>
    /// 通过 AddMcpServer().WithToolsFromAssembly() 注册后，MCP server 应能发现 CatalogTools 的两个工具。
    /// </summary>
    [Fact]
    public void CatalogTools_AreDiscoveredViaWithToolsFromAssembly()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<INodeRegistry>(new StubNodeRegistry([], []));
        services.AddSingleton<CatalogService>();
        services.AddMcpServer()
            .WithToolsFromAssembly(typeof(CatalogTools).Assembly);

        var provider = services.BuildServiceProvider();
        var tools = provider.GetServices<McpServerTool>().ToList();

        var toolNames = tools.Select(t => t.ProtocolTool.Name).ToList();
        Assert.Contains("list_node_catalog", toolNames);
        Assert.Contains("get_node_detail", toolNames);
    }

    private static CatalogService CreateCatalogService(IReadOnlyList<NodeTypeDescriptor> descriptors)
    {
        var nodes = descriptors.Select(d => new FakeNodeType(d.TypeName, d.Category)).ToList<INodeType>();
        var registryMock = new Mock<INodeRegistry>();
        registryMock.Setup(r => r.GetDescriptors()).Returns(descriptors);
        registryMock.Setup(r => r.GetAll()).Returns(nodes);

        foreach (var descriptor in descriptors)
        {
            registryMock.Setup(r => r.TryGet(descriptor.TypeName, out It.Ref<INodeType?>.IsAny))
                .Returns((string _, out INodeType? node) =>
                {
                    node = nodes.FirstOrDefault(n => n.TypeName.Equals(descriptor.TypeName, StringComparison.OrdinalIgnoreCase));
                    return node is not null;
                });
            registryMock.Setup(r => r.GetDescriptor(descriptor.TypeName)).Returns(descriptor);
        }

        registryMock.Setup(r => r.TryGet(It.Is<string>(n => !descriptors.Any(d => d.TypeName.Equals(n, StringComparison.OrdinalIgnoreCase))), out It.Ref<INodeType?>.IsAny))
            .Returns((string _, out INodeType? node) =>
            {
                node = null;
                return false;
            });

        return new CatalogService(registryMock.Object);
    }

    private static NodeTypeDescriptor CreateDescriptor(string typeName, string category)
    {
        return new NodeTypeDescriptor
        {
            TypeName = typeName,
            DisplayName = typeName,
            Category = category,
            Parameters = [],
            Ports = [],
        };
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

    private sealed class FakeNodeType : INodeType
    {
        public FakeNodeType(string typeName, string category)
        {
            TypeName = typeName;
            DisplayName = typeName;
            Category = category;
        }

        public string TypeName { get; }

        public string DisplayName { get; }

        public string Category { get; }

        public string Icon { get; } = string.Empty;

        public ExecutionMode ExecutionMode { get; } = ExecutionMode.OnceForAll;

        public IReadOnlyList<PortDefinition> Ports { get; } = [];

        public bool DefaultIsEntry { get; }

        public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<NodeExecutionResult>(null!);
        }
    }
}

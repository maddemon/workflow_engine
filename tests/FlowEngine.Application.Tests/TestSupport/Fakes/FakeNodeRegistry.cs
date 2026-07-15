using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;

namespace FlowEngine.Application.Tests.TestSupport.Fakes;

public sealed class FakeNodeRegistry : INodeRegistry
{
    private readonly IReadOnlyCollection<NodeTypeDescriptor> _descriptors;

    public FakeNodeRegistry()
        : this([])
    {
    }

    public FakeNodeRegistry(IReadOnlyCollection<NodeTypeDescriptor> descriptors)
    {
        _descriptors = descriptors;
    }

    public void Register(INodeType nodeType) { }

    public INodeType Get(string typeName) => throw new InvalidOperationException();

    public bool TryGet(string typeName, out INodeType? nodeType)
    {
        nodeType = null;
        return false;
    }

    public IReadOnlyCollection<INodeType> GetAll() => [];

    public INodeType CreateInstance(string typeName) => throw new InvalidOperationException();

    public IReadOnlyCollection<NodeTypeDescriptor> GetDescriptors() => _descriptors;

    public NodeTypeDescriptor GetDescriptor(string typeName)
    {
        var descriptor = _descriptors.FirstOrDefault(d => d.TypeName.Equals(typeName, StringComparison.OrdinalIgnoreCase));
        if (descriptor is null)
        {
            throw new InvalidOperationException($"Node type '{typeName}' is not registered.");
        }

        return descriptor;
    }
}

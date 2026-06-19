using System.Collections.Concurrent;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;

namespace FlowEngine.Runtime.Tests.Executor;

/// <summary>
/// 内存中的工作流仓储，用于执行引擎单元测试。
/// </summary>
public sealed class InMemoryWorkflowRepository : IWorkflowRepository
{
    private readonly ConcurrentDictionary<Guid, SortedDictionary<int, Workflow>> _store = new();

    /// <inheritdoc />
    public Task<Workflow?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (!_store.TryGetValue(id, out var versions) || versions.Count == 0)
        {
            return Task.FromResult<Workflow?>(null);
        }

        var latest = versions.Values.Last();
        return Task.FromResult<Workflow?>(Clone(latest));
    }

    /// <inheritdoc />
    public Task<IReadOnlyCollection<Workflow>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var result = _store.Values
            .Where(v => v.Count > 0)
            .Select(v => Clone(v.Values.Last()))
            .ToList();

        return Task.FromResult<IReadOnlyCollection<Workflow>>(result);
    }

    /// <inheritdoc />
    public Task SaveAsync(Workflow workflow, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        var versions = _store.GetOrAdd(workflow.Id, _ => new SortedDictionary<int, Workflow>());
        var nextVersion = versions.Count > 0 ? versions.Keys.Max() + 1 : 1;

        var clone = Clone(workflow);
        clone.Version = nextVersion;
        versions[nextVersion] = clone;

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _store.TryRemove(id, out _);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<Workflow?> GetByVersionAsync(Guid id, int version, CancellationToken cancellationToken = default)
    {
        if (!_store.TryGetValue(id, out var versions) || !versions.TryGetValue(version, out var workflow))
        {
            return Task.FromResult<Workflow?>(null);
        }

        return Task.FromResult<Workflow?>(Clone(workflow));
    }

    /// <inheritdoc />
    public Task<IReadOnlyCollection<int>> GetVersionsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (!_store.TryGetValue(id, out var versions))
        {
            return Task.FromResult<IReadOnlyCollection<int>>(Array.Empty<int>());
        }

        return Task.FromResult<IReadOnlyCollection<int>>(versions.Keys.ToList());
    }

    private static Workflow Clone(Workflow source)
    {
        return new Workflow
        {
            Id = source.Id,
            ProjectId = source.ProjectId,
            Name = source.Name,
            Version = source.Version,
            CreatedBy = source.CreatedBy,
            CreatedAt = source.CreatedAt,
            UpdatedAt = source.UpdatedAt,
            IsActive = source.IsActive,
            Nodes = source.Nodes.ToList(),
            Connections = source.Connections.ToList()
        };
    }
}

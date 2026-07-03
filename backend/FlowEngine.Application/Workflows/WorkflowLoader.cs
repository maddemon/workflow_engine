using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace FlowEngine.Application.Workflows;

/// <summary>
/// 基于数据库的工作流加载器实现。
/// </summary>
public sealed class WorkflowLoader(FlowEngineDbContext dbContext) : IWorkflowLoader
{
    /// <inheritdoc />
    public async Task<Workflow?> LoadAsync(Guid workflowId, CancellationToken cancellationToken = default)
    {
        return await dbContext.Workflows
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == workflowId, cancellationToken)
            .ConfigureAwait(false);
    }
}

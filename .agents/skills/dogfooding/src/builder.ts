import type { McpClient } from './mcp-client.js';
import type { LlmClient } from './llm-client.js';
import type { Scenario, BuilderTrace, McpStep, FinalStatus } from './types.js';

interface BuilderConfig {
  maxBuildRetries: number;
  maxExecRetries: number;
}

export class Builder {
  constructor(
    private mcp: McpClient,
    private llm: LlmClient,
    private config: BuilderConfig,
  ) {}

  async build(scenario: Scenario): Promise<BuilderTrace> {
    const steps: McpStep[] = [];
    let totalMcpCalls = 0;
    let aiRetries = 0;
    let finalStatus: FinalStatus = 'failed';

    const log = (phase: string, tool: string, request: unknown, response: unknown, errors?: McpStep['errors']) => {
      steps.push({ phase, tool, request, response, errors: errors ?? null, timestamp: new Date().toISOString() });
      totalMcpCalls++;
    };

    try {
      // Phase: 发现
      const conventions = await this.mcp.callTool<unknown>('get_conventions', {});
      log('discover', 'get_conventions', {}, conventions);

      const catalog = await this.mcp.callTool<Array<{ typeName: string; category: string }>>('list_node_catalog', {});
      log('discover', 'list_node_catalog', {}, catalog);

      const nodeTypesPrompt = `需求: ${scenario.description}\n\n可用节点:\n${catalog.map((n: { typeName: string; category: string }) => `- ${n.typeName} (${n.category})`).join('\n')}\n\n选择 2-5 个节点类型，逗号分隔。`;
      const chosen = (await this.llm.generate(nodeTypesPrompt)).split(',').map(s => s.trim());
      const chosenTypes = chosen.filter(c => catalog.some((n: { typeName: string }) => n.typeName === c));

      const nodeDetails: Array<{ typeName: string; inputSchema: unknown }> = [];
      for (const typeName of chosenTypes) {
        const detail = await this.mcp.callTool<{ typeName: string; inputSchema: unknown }>('get_node_detail', { name: typeName });
        log('discover', 'get_node_detail', { name: typeName }, detail);
        nodeDetails.push(detail);
      }

      // Phase: 构建 — assemble + validate 自纠
      const draftResult = await this.mcp.callTool<{ draftId: string; workflow: unknown }>('assemble_workflow', {
        name: scenario.title, nodes: [], connections: [],
      });
      log('build', 'assemble_workflow', { name: scenario.title }, draftResult);
      let workflowId = draftResult.draftId;

      let buildRetries = 0;
      while (buildRetries <= this.config.maxBuildRetries) {
        const validationResult = await this.mcp.callTool<{ success: boolean; errors?: Array<{ nodeId: string; field: string; errorType: string; canAutoFix: boolean; suggestedFix?: string }> }>('validate_workflow', { workflowId });
        log('validate', 'validate_workflow', { workflowId }, validationResult, validationResult.errors?.map(e => ({
          nodeId: e.nodeId, field: e.field, errorType: e.errorType, message: '', canAutoFix: e.canAutoFix, suggestedFix: e.suggestedFix,
        })) ?? null);

        if (validationResult.success) break;
        if (buildRetries >= this.config.maxBuildRetries) {
          finalStatus = 'blocker';
          return { scenarioId: scenario.id, steps, totalMcpCalls, aiRetries, finalStatus };
        }

        const feedback = await this.mcp.callTool<{ rejectionReason?: string; draftStatus: string }>('get_draft_feedback', { draftId: workflowId });
        log('build', 'get_draft_feedback', { draftId: workflowId }, feedback);

        const modifyResult = await this.mcp.callTool<{ draftId: string; workflow: unknown; diff: unknown[] }>('modify_workflow', {
          workflowId,
          operations: validationResult.errors?.map(e => ({
            op: 'modify', nodeId: e.nodeId, path: `/parameters/${e.field}`, value: e.suggestedFix ?? '',
          })) ?? [],
        });
        log('build', 'modify_workflow', { workflowId, operations: validationResult.errors }, modifyResult);
        workflowId = modifyResult.draftId;
        aiRetries++;
        buildRetries++;
      }

      // Phase: 确认（可能被拒后自纠）
      let confirmResult = await this.mcp.callTool<{ id: string; isActive: boolean; rejectionReason?: string }>('confirm_workflow', { draftId: workflowId });
      log('confirm', 'confirm_workflow', { draftId: workflowId }, confirmResult);

      if (!confirmResult.isActive) {
        for (let c = 0; c < 2; c++) {
          const feedback = await this.mcp.callTool<{ rejectionReason?: string; draftStatus: string }>('get_draft_feedback', { draftId: workflowId });
          log('confirm', 'get_draft_feedback', { draftId: workflowId }, feedback);

          const modifyResult = await this.mcp.callTool<{ draftId: string }>('modify_workflow', {
            workflowId, operations: [{ op: 'modify', nodeId: '*', path: '/rejectionReason', value: feedback.rejectionReason ?? '' }],
          });
          log('confirm', 'modify_workflow', { workflowId, operations: [{ op: 'modify', nodeId: '*', path: '/rejectionReason' }] }, modifyResult);
          workflowId = modifyResult.draftId;
          aiRetries++;

          confirmResult = await this.mcp.callTool<{ id: string; isActive: boolean }>('confirm_workflow', { draftId: workflowId });
          log('confirm', 'confirm_workflow', { draftId: workflowId }, confirmResult);
          if (confirmResult.isActive) break;
        }
      }

      if (!confirmResult.isActive) {
        finalStatus = 'failed';
        return { scenarioId: scenario.id, steps, totalMcpCalls, aiRetries, finalStatus };
      }

      // Phase: 执行
      let execResult = await this.mcp.callTool<{ executionId: string; status: string }>('execute_workflow', { workflowId: confirmResult.id });
      log('execute', 'execute_workflow', { workflowId: confirmResult.id }, execResult);

      if (execResult.status !== 'Completed') {
        for (let e = 0; e < this.config.maxExecRetries; e++) {
          const feedback = await this.mcp.callTool<{ rejectionReason?: string }>('get_draft_feedback', { draftId: confirmResult.id });
          log('execute', 'get_draft_feedback', { draftId: confirmResult.id }, feedback);

          const modifyResult = await this.mcp.callTool<{ draftId: string }>('modify_workflow', { workflowId: confirmResult.id, operations: [] });
          log('execute', 'modify_workflow', { workflowId: confirmResult.id }, modifyResult);
          workflowId = modifyResult.draftId;
          aiRetries++;

          const reConfirm = await this.mcp.callTool<{ id: string; isActive: boolean }>('confirm_workflow', { draftId: workflowId });
          log('execute', 'confirm_workflow', { draftId: workflowId }, reConfirm);
          execResult = await this.mcp.callTool<{ executionId: string; status: string }>('execute_workflow', { workflowId: reConfirm.id });
          log('execute', 'execute_workflow', { workflowId: reConfirm.id }, execResult);
          if (execResult.status === 'Completed') break;
        }
      }

      finalStatus = execResult.status === 'Completed' ? 'completed' : 'failed';
    } catch (err) {
      finalStatus = 'blocker';
      steps.push({
        phase: 'error', tool: 'unknown', request: {}, response: String(err),
        errors: [{ errorType: 'UnhandledError', message: String(err), canAutoFix: false }],
        timestamp: new Date().toISOString(),
      });
    }

    return { scenarioId: scenario.id, steps, totalMcpCalls, aiRetries, finalStatus };
  }
}

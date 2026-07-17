import { describe, it, expect, vi, beforeEach } from 'vitest';
import { Builder } from '../src/builder.js';
import type { Scenario, BuilderTrace } from '../src/types.js';

describe('Builder', () => {
  const mockMcp = { initialize: vi.fn(), callTool: vi.fn(), listTools: vi.fn(), close: vi.fn() };

  beforeEach(() => {
    vi.clearAllMocks();
    mockMcp.initialize.mockResolvedValue([]);
  });

  const sampleScenario: Scenario = {
    id: 's-test', title: 'HTTP + Code', description: 'Fetch data then process',
    difficulty: 'medium', categoryCoverage: ['http', 'core'],
  };

  const mockMcpSuccessSequence = () => {
    mockMcp.callTool.mockResolvedValueOnce({ expressionLanguage: 'javascript', rules: [] });
    mockMcp.callTool.mockResolvedValueOnce([{ name: 'httpRequest', category: 'http' }, { name: 'code', category: 'core' }]);
    mockMcp.callTool.mockResolvedValueOnce({ name: 'httpRequest', inputSchema: { properties: { url: { type: 'string' } } } });
    mockMcp.callTool.mockResolvedValueOnce({ name: 'code', inputSchema: { properties: { code: { type: 'string' } } } });
    mockMcp.callTool.mockResolvedValueOnce({ draftId: 'draft-1', workflow: { name: 'test' } });
    mockMcp.callTool.mockResolvedValueOnce({ success: true, errors: [] });
    mockMcp.callTool.mockResolvedValueOnce({ id: 'wf-1', isActive: true });
    // happy path modify + re-confirm
    mockMcp.callTool.mockResolvedValueOnce({ draftId: 'draft-2', workflow: {} });
    mockMcp.callTool.mockResolvedValueOnce({ id: 'wf-1', isActive: true });
    // execute
    mockMcp.callTool.mockResolvedValueOnce({ execution: { id: 'exec-1', status: 'Completed' } });
  };

  it('build - completes happy path successfully', async () => {
    mockMcpSuccessSequence();
    const builder = new Builder(mockMcp as any, { maxBuildRetries: 3, maxExecRetries: 2 });
    const trace = await builder.build(sampleScenario);
    expect(trace.finalStatus).toBe('completed');
    expect(trace.scenarioId).toBe('s-test');
    expect(trace.totalMcpCalls).toBe(10);
  });

  it('build - handles validate errors with retry', async () => {
    let callCount = 0;
    mockMcp.callTool.mockImplementation(async (name: string) => {
      callCount++;
      if (name === 'get_conventions') return { expressionLanguage: 'javascript', rules: [] };
      if (name === 'list_node_catalog') return [{ name: 'httpRequest', category: 'http' }, { name: 'code', category: 'core' }];
      if (name === 'get_node_detail') return { name: 'httpRequest', inputSchema: {} };
      if (name === 'assemble_workflow') return { draftId: 'draft-1', workflow: {} };
      if (name === 'validate_workflow') {
        return callCount <= 6
          ? { success: false, errors: [{ nodeId: 'fetch', field: 'url', errorType: 'InvalidExpression', canAutoFix: true, suggestedFix: 'Use JS concat' }] }
          : { success: true, errors: [] };
      }
      if (name === 'get_draft_feedback') return { draftStatus: 'Pending', rejectionReason: 'Expression syntax error' };
      if (name === 'modify_workflow') return { draftId: 'draft-2', workflow: {}, diff: [] };
      if (name === 'confirm_workflow') return { id: 'wf-1', isActive: true };
      if (name === 'execute_workflow') return { execution: { id: 'exec-1', status: 'Completed' } };
      return {};
    });
    const builder = new Builder(mockMcp as any, { maxBuildRetries: 3, maxExecRetries: 2 });
    const trace = await builder.build(sampleScenario);
    expect(trace.finalStatus).toBe('completed');
    expect(trace.aiRetries).toBe(1);
  });

  it('build - marks BLOCKER after max retries exceeded', async () => {
    mockMcp.callTool.mockImplementation(async (name: string) => {
      if (name === 'get_conventions') return { expressionLanguage: 'javascript', rules: [] };
      if (name === 'list_node_catalog') return [{ name: 'httpRequest', category: 'http' }];
      if (name === 'get_node_detail') return { name: 'httpRequest', inputSchema: {} };
      if (name === 'assemble_workflow') return { draftId: 'draft-1', workflow: {} };
      if (name === 'validate_workflow') return { success: false, errors: [{ nodeId: 'fetch', field: 'url', errorType: 'InvalidExpression', canAutoFix: true, suggestedFix: 'fix' }] };
      if (name === 'get_draft_feedback') return { draftStatus: 'Pending', rejectionReason: 'Error' };
      if (name === 'modify_workflow') return { draftId: 'draft-2', workflow: {} };
      return {};
    });
    const builder = new Builder(mockMcp as any, { maxBuildRetries: 3, maxExecRetries: 2 });
    const trace = await builder.build(sampleScenario);
    expect(trace.finalStatus).toBe('blocker');
    expect(trace.aiRetries).toBe(3);
  });

  it('build - logs all MCP calls in trace', async () => {
    mockMcpSuccessSequence();
    const builder = new Builder(mockMcp as any, { maxBuildRetries: 3, maxExecRetries: 2 });
    const trace = await builder.build(sampleScenario);
    expect(trace.steps.length).toBeGreaterThan(0);
    expect(trace.steps[0].tool).toBe('get_conventions');
    expect(trace.steps[0].phase).toBe('discover');
  });
});

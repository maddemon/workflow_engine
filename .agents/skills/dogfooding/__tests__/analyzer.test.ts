import { describe, it, expect, vi } from 'vitest';
import { Analyzer } from '../src/analyzer.js';
import type { BuilderTrace, ScenarioAnalysis } from '../src/types.js';

describe('Analyzer', () => {
  const mockKb = { loadErrorPatterns: () => [], appendErrorPattern: vi.fn() };

  it('analyzeTraces - classifies mustache error as A2', () => {
    const traces: BuilderTrace[] = [{
      scenarioId: 's1', finalStatus: 'failed',
      steps: [{
        phase: 'validate', tool: 'validate_workflow',
        request: {}, response: { success: false, errors: [{ nodeId: 'fetch', field: 'url', errorType: 'InvalidExpression', message: '含有 n8n 风格的 {{ }} 模板', canAutoFix: true, suggestedFix: '改用 JS' }] },
        errors: [{ errorType: 'InvalidExpression', message: '含有 n8n 风格的 {{ }} 模板', canAutoFix: true, suggestedFix: '改用 JS' }],
        timestamp: '',
      }],
      totalMcpCalls: 5, aiRetries: 1,
    }];

    const analysis = new Analyzer(mockKb as any).analyzeTraces(traces);
    expect(analysis).toHaveLength(1);
    expect(analysis[0].issues[0].category).toBe('A');
    expect(analysis[0].issues[0].subCategory).toBe('A2');
  });

  it('analyzeTraces - classifies 500 error as C4', () => {
    const traces: BuilderTrace[] = [{
      scenarioId: 's2', finalStatus: 'blocker',
      steps: [{
        phase: 'error', tool: 'unknown', request: {}, response: 'MCP HTTP 500: Internal server error',
        errors: [{ errorType: 'UnhandledError', message: 'MCP HTTP 500: Internal server error', canAutoFix: false }],
        timestamp: '',
      }],
      totalMcpCalls: 3, aiRetries: 0,
    }];

    const analysis = new Analyzer(mockKb as any).analyzeTraces(traces);
    expect(analysis[0].issues[0].category).toBe('C');
    expect(analysis[0].issues[0].subCategory).toBe('C4');
  });

  it('analyzeTraces - classifies credential error as D1', () => {
    const traces: BuilderTrace[] = [{
      scenarioId: 's3', finalStatus: 'failed',
      steps: [{
        phase: 'execute', tool: 'execute_workflow',
        request: {}, response: { status: 'Failed', error: 'Credential not found: database-conn' },
        errors: [{ errorType: 'ExecutionError', message: 'Credential not found', canAutoFix: false }],
        timestamp: '',
      }],
      totalMcpCalls: 6, aiRetries: 0,
    }];

    const analysis = new Analyzer(mockKb as any).analyzeTraces(traces);
    expect(analysis[0].issues[0].category).toBe('D');
    expect(analysis[0].issues[0].subCategory).toBe('D1');
  });

  it('computeMetrics - calculates correct rates', () => {
    const traces: BuilderTrace[] = [
      { scenarioId: 's1', finalStatus: 'completed', steps: [], totalMcpCalls: 8, aiRetries: 0 },
      { scenarioId: 's2', finalStatus: 'completed', steps: [], totalMcpCalls: 12, aiRetries: 1 },
      { scenarioId: 's3', finalStatus: 'failed', steps: [], totalMcpCalls: 6, aiRetries: 2 },
    ];

    const analysis: ScenarioAnalysis[] = [
      { scenarioId: 's1', finalStatus: 'completed', issues: [] },
      { scenarioId: 's2', finalStatus: 'completed', issues: [{ category: 'A', subCategory: 'A2', description: '', rootCause: '', fixType: 'convention_update' }] },
      { scenarioId: 's3', finalStatus: 'failed', issues: [{ category: 'C', subCategory: 'C1', description: '', rootCause: '', fixType: 'code_bug' }] },
    ];

    const metrics = Analyzer.computeMetrics(traces, analysis);
    expect(metrics.firstAttemptSuccessRate).toBeCloseTo(1 / 3); // 只有 s1 无重试
    expect(metrics.avgRetriesPerScenario).toBeCloseTo(1);
    expect(metrics.aCategoryPct).toBeCloseTo(0.5);
    expect(metrics.cCategoryPct).toBeCloseTo(0.5);
  });
});

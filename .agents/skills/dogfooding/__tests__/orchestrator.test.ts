import { describe, it, expect, vi, beforeEach } from 'vitest';
import { Orchestrator } from '../src/orchestrator.js';
import type { Scenario } from '../src/types.js';

describe('Orchestrator', () => {
  const mockGen = { generate: vi.fn() };
  const mockBuilder = { build: vi.fn() };
  const mockAnalyzer = { analyzeTraces: vi.fn(), computeMetrics: vi.fn() };
  const mockImprover = { process: vi.fn() };
  const mockKb = { saveRunReport: vi.fn() };
  const mockMcp = { initialize: vi.fn(), close: vi.fn() };

  const sampleScenarios: Scenario[] = [
    { id: 's1', title: 'Test 1', description: 'D1', difficulty: 'easy', categoryCoverage: ['http'] },
  ];

  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('runRound - completes full cycle', async () => {
    mockBuilder.build.mockResolvedValue({ scenarioId: 's1', steps: [], finalStatus: 'completed', totalMcpCalls: 8, aiRetries: 0 });
    mockAnalyzer.analyzeTraces.mockReturnValue([{ scenarioId: 's1', finalStatus: 'completed', issues: [] }]);
    mockAnalyzer.computeMetrics.mockReturnValue({
      firstAttemptSuccessRate: 1, avgRetriesPerScenario: 0,
      aCategoryPct: 0, bCategoryPct: 0, cCategoryPct: 0, dCategoryPct: 0,
      selfHealRate: 1, blockerCount: 0,
    });
    mockImprover.process.mockResolvedValue({ fixAttempted: 0, fixSkipped: 0, prUrls: [] });
    mockMcp.initialize.mockResolvedValue([]);

    const orchestrator = new Orchestrator(
      mockMcp as any, mockGen as any, mockBuilder as any,
      mockAnalyzer as any, mockImprover as any, mockKb as any,
      2, 1,
    );

    const report = await orchestrator.runRound('round-test', sampleScenarios);

    expect(report.roundId).toBe('round-test');
    expect(report.scenarios).toHaveLength(1);
    expect(report.summary.totalScenarios).toBe(1);
    expect(mockKb.saveRunReport).toHaveBeenCalledWith(report);
  });

  it('runRound - blocks on MCP initialization failure', async () => {
    mockMcp.initialize.mockRejectedValue(new Error('Connection refused'));

    const orchestrator = new Orchestrator(
      mockMcp as any, mockGen as any, mockBuilder as any,
      mockAnalyzer as any, mockImprover as any, mockKb as any,
      2, 1,
    );

    await expect(orchestrator.runRound('round-1', sampleScenarios)).rejects.toThrow('Connection refused');
  });

  it('runRound - handles empty scenario list', async () => {
    mockMcp.initialize.mockResolvedValue([]);

    const orchestrator = new Orchestrator(
      mockMcp as any, mockGen as any, mockBuilder as any,
      mockAnalyzer as any, mockImprover as any, mockKb as any,
      2, 1,
    );

    const report = await orchestrator.runRound('round-empty', []);
    expect(report.summary.totalScenarios).toBe(0);
  });
});

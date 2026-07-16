import { describe, it, expect, vi, beforeEach } from 'vitest';
import { ScenarioGenerator } from '../src/scenario-generator.js';

describe('ScenarioGenerator', () => {
  const mockMcp = { initialize: vi.fn(), callTool: vi.fn(), listTools: vi.fn() };
  const mockKb = { loadCoverage: vi.fn(), recordScenario: vi.fn() };

  const catalog = [
    { typeName: 'httpRequest', displayName: 'HTTP Request', category: 'http', description: 'Make HTTP calls' },
    { typeName: 'manualTrigger', displayName: 'Manual Trigger', category: 'trigger', description: 'Manual trigger' },
    { typeName: 'code', displayName: 'Code', category: 'core', description: 'Run JavaScript code' },
  ];

  beforeEach(() => {
    vi.clearAllMocks();
    mockKb.loadCoverage.mockReturnValue({ scenarioCount: 0, coveredNodePairs: [], coveredCategories: [] });
    mockMcp.callTool.mockResolvedValue(catalog);
  });

  it('generate - returns requested number of scenarios', async () => {
    const gen = new ScenarioGenerator(mockMcp as any, mockKb as any);
    const scenarios = await gen.generate(2);
    expect(scenarios).toHaveLength(2);
    expect(scenarios[0].id).toBeTruthy();
    expect(scenarios[0].title).toContain('HTTP Request');
  });

  it('generate - records all scenarios to KB', async () => {
    const gen = new ScenarioGenerator(mockMcp as any, mockKb as any);
    const scenarios = await gen.generate(3);
    expect(mockKb.recordScenario).toHaveBeenCalledTimes(3);
    for (const s of scenarios) {
      expect(mockKb.recordScenario).toHaveBeenCalledWith(s);
    }
  });

  it('generate - throws on empty catalog', async () => {
    mockMcp.callTool.mockResolvedValue([]);
    const gen = new ScenarioGenerator(mockMcp as any, mockKb as any);
    await expect(gen.generate(1)).rejects.toThrow('节点目录为空');
  });

  it('generate - uses selected node categories in categoryCoverage', async () => {
    const gen = new ScenarioGenerator(mockMcp as any, mockKb as any);
    const scenarios = await gen.generate(1);
    expect(scenarios[0].categoryCoverage.length).toBeGreaterThanOrEqual(2);
  });
});

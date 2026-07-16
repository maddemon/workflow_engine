import { describe, it, expect, vi, beforeEach } from 'vitest';
import { ScenarioGenerator } from '../src/scenario-generator.js';

describe('ScenarioGenerator', () => {
  const mockLlm = { generate: vi.fn(), generateJson: vi.fn() };
  const mockMcp = { initialize: vi.fn(), callTool: vi.fn(), listTools: vi.fn() };
  const mockKb = { loadCoverage: vi.fn(), recordScenario: vi.fn() };

  beforeEach(() => {
    vi.clearAllMocks();
    mockKb.loadCoverage.mockReturnValue({ scenarioCount: 0, coveredNodePairs: [], coveredCategories: [] });
    mockMcp.callTool.mockResolvedValue([
      { typeName: 'httpRequest', displayName: 'HTTP Request', category: 'http', description: 'Make HTTP calls' },
      { typeName: 'manualTrigger', displayName: 'Manual Trigger', category: 'trigger', description: 'Manual trigger' },
      { typeName: 'code', displayName: 'Code', category: 'core', description: 'Run JavaScript code' },
    ]);
  });

  it('generate - returns requested number of scenarios', async () => {
    mockLlm.generateJson.mockResolvedValue([
      { id: 's1', title: 'Fetch data and process', description: 'Use HTTP to fetch and code to process', difficulty: 'medium', categoryCoverage: ['http', 'core'] },
      { id: 's2', title: 'Trigger notification', description: 'Manual trigger sends HTTP notification', difficulty: 'easy', categoryCoverage: ['trigger', 'http'] },
    ]);

    const gen = new ScenarioGenerator(mockLlm as any, mockMcp as any, mockKb as any);
    const scenarios = await gen.generate(2);
    expect(scenarios).toHaveLength(2);
    expect(scenarios[0].id).toBe('s1');
    expect(mockKb.recordScenario).toHaveBeenCalledTimes(2);
  });

  it('generate - includes catalog context in prompt', async () => {
    mockLlm.generateJson.mockResolvedValue([]);

    const gen = new ScenarioGenerator(mockLlm as any, mockMcp as any, mockKb as any);
    await gen.generate(1);

    const promptArg = mockLlm.generateJson.mock.calls[0][0];
    expect(promptArg).toContain('httpRequest');
    expect(promptArg).toContain('manualTrigger');
    expect(promptArg).toContain('categoryCoverage');
  });

  it('generate - avoids already-covered categories', async () => {
    mockKb.loadCoverage.mockReturnValue({ scenarioCount: 5, coveredNodePairs: [['http', 'db']], coveredCategories: ['http', 'db', 'trigger'] });
    mockLlm.generateJson.mockResolvedValue([
      { id: 's1', title: 'Code only', description: 'Just code', difficulty: 'easy', categoryCoverage: ['core'] },
    ]);

    const gen = new ScenarioGenerator(mockLlm as any, mockMcp as any, mockKb as any);
    await gen.generate(1);
    const promptArg = mockLlm.generateJson.mock.calls[0][0];
    expect(promptArg).toContain('core');
  });
});

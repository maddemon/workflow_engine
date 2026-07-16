import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { Improver } from '../src/improver.js';
import { mkdtempSync, rmSync, mkdirSync } from 'fs';
import { join } from 'path';
import { tmpdir } from 'os';
import type { ScenarioAnalysis } from '../src/types.js';

describe('Improver', () => {
  let tmpDir: string;
  let mockKb: any;
  let mockExec: any;

  beforeEach(() => {
    tmpDir = mkdtempSync(join(tmpdir(), 'improver-'));
    mkdirSync(join(tmpDir, 'runs'), { recursive: true });
    mockExec = vi.fn();
    mockKb = {
      baseDir: tmpDir,
      loadErrorPatterns: vi.fn(() => []),
      appendErrorPattern: vi.fn(),
      loadCoverage: vi.fn(() => ({ scenarioCount: 0, coveredNodePairs: [], coveredCategories: [] })),
    };
  });

  afterEach(() => {
    rmSync(tmpDir, { recursive: true, force: true });
  });

  it('process - appends error patterns for all issues', async () => {
    const analyses: ScenarioAnalysis[] = [{
      scenarioId: 's1', finalStatus: 'failed',
      issues: [
        { category: 'A', subCategory: 'A2', description: 'Mustache syntax', rootCause: 'n8n habit', fixType: 'convention_update', targetFiles: ['ConventionTools.cs'], confidence: 'high', estimatedEffort: 'small' },
        { category: 'C', subCategory: 'C1', description: 'Validation missing', rootCause: 'No mustache scan', fixType: 'code_bug', targetFiles: ['Validator.cs'], confidence: 'high', estimatedEffort: 'small' },
      ],
    }];

    const improver = new Improver(mockKb as any, { exec: mockExec });
    const result = await improver.process(analyses, 'round-01');

    expect(mockKb.appendErrorPattern).toHaveBeenCalledTimes(2);
    expect(result.fixAttempted).toBeGreaterThan(0);
    expect(result.fixSkipped).toBe(0);
  });

  it('process - attempts PR for high-confidence code_bug', async () => {
    mockExec.mockResolvedValue({ stdout: 'https://github.com/flowengine/pull/1', stderr: '' });

    const analyses: ScenarioAnalysis[] = [{
      scenarioId: 's1', finalStatus: 'failed',
      issues: [{
        category: 'C', subCategory: 'C1', description: 'Mustache validation missing',
        rootCause: 'WorkflowDraftValidator 缺乏词法扫描', fixType: 'code_bug',
        targetFiles: ['backend/FlowEngine.Application/Workflows/WorkflowDraftValidator.cs'],
        confidence: 'high', estimatedEffort: 'small',
        proposedFix: '添加 CollectMustacheErrors 扫描 {{ }}',
      }],
    }];

    const improver = new Improver(mockKb as any, { exec: mockExec });
    const result = await improver.process(analyses, 'round-01');

    // gh pr create 应被调用
    const prCmd = mockExec.mock.calls.find((c: string[]) => c[0].includes('gh pr create'));
    expect(prCmd).toBeTruthy();
    expect(result.fixAttempted).toBe(1);
  });

  it('process - skips low-confidence issues', async () => {
    const analyses: ScenarioAnalysis[] = [{
      scenarioId: 's1', finalStatus: 'failed',
      issues: [{
        category: 'C', subCategory: 'C4', description: 'Unknown error',
        rootCause: 'Unknown', fixType: 'code_bug',
        confidence: 'low', estimatedEffort: 'large',
      }],
    }];

    const improver = new Improver(mockKb as any, { exec: mockExec });
    const result = await improver.process(analyses, 'round-01');

    expect(result.fixAttempted).toBe(0);
    expect(result.fixSkipped).toBe(1);
  });
});

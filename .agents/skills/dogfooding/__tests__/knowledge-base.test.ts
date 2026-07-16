import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { mkdtempSync, rmSync, existsSync, readFileSync, mkdirSync, writeFileSync } from 'fs';
import { join } from 'path';
import { tmpdir } from 'os';
import { KnowledgeBase } from '../src/knowledge-base.js';
import type { Scenario, RoundReport, ErrorPattern } from '../src/types.js';

describe('KnowledgeBase', () => {
  let tmpDir: string;
  let kb: KnowledgeBase;

  beforeEach(() => {
    tmpDir = mkdtempSync(join(tmpdir(), 'kb-test-'));
    mkdirSync(join(tmpDir, 'runs'), { recursive: true });
    kb = new KnowledgeBase(tmpDir);
  });

  afterEach(() => {
    rmSync(tmpDir, { recursive: true, force: true });
  });

  it('coverage - round-trips correctly', () => {
    const cov = { scenarioCount: 5, coveredNodePairs: [['http', 'db']], coveredCategories: ['http', 'db'] };
    kb.saveCoverage(cov);
    const loaded = kb.loadCoverage();
    expect(loaded.scenarioCount).toBe(5);
    expect(loaded.coveredNodePairs).toEqual([['http', 'db']]);
  });

  it('coverage - returns default when file missing', () => {
    const loaded = kb.loadCoverage();
    expect(loaded.scenarioCount).toBe(0);
    expect(loaded.coveredNodePairs).toEqual([]);
  });

  it('error patterns - appends and loads', () => {
    const pattern: ErrorPattern = {
      id: 'ep-1', category: 'B', subCategory: 'B4',
      description: 'test', rootCause: 'test',
      firstSeen: 'round-01', lastSeen: 'round-01',
      occurrenceCount: 1, fixStatus: 'pending',
    };
    kb.appendErrorPattern(pattern);
    const patterns = kb.loadErrorPatterns();
    expect(patterns).toHaveLength(1);
    expect(patterns[0].id).toBe('ep-1');
  });

  it('run report - saves to runs/ directory', () => {
    const report: RoundReport = {
      roundId: 'round-01', date: '2026-07-16',
      scenarios: [], analyses: [],
      summary: { totalScenarios: 0, completed: 0, failed: 0, blocker: 0, totalIssues: 0, byCategory: {}, fixableIssues: 0 },
      metrics: { firstAttemptSuccessRate: 0, avgRetriesPerScenario: 0, aCategoryPct: 0, bCategoryPct: 0, cCategoryPct: 0, dCategoryPct: 0, selfHealRate: 0, blockerCount: 0 },
    };
    kb.saveRunReport(report);
    expect(existsSync(join(tmpDir, 'runs', 'round-01.json'))).toBe(true);
  });

  it('recordScenario - updates coverage correctly', () => {
    const scenario: Scenario = { id: 's1', title: 'Test', description: '', difficulty: 'easy', categoryCoverage: ['http', 'db'] };
    kb.recordScenario(scenario);
    const cov = kb.loadCoverage();
    expect(cov.scenarioCount).toBe(1);
    expect(cov.coveredCategories).toContain('http');
  });
});

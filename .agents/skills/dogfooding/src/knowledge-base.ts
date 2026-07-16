import { readFileSync, writeFileSync, existsSync, mkdirSync } from 'fs';
import { join } from 'path';
import type { Coverage, ErrorPattern, RoundReport, Scenario } from './types.js';

export class KnowledgeBase {
  constructor(private baseDir: string) {
    mkdirSync(join(baseDir, 'runs'), { recursive: true });
  }

  // ── Coverage ──
  loadCoverage(): Coverage {
    const path = join(this.baseDir, 'coverage.json');
    if (!existsSync(path)) return { scenarioCount: 0, coveredNodePairs: [], coveredCategories: [] };
    return JSON.parse(readFileSync(path, 'utf-8'));
  }

  saveCoverage(coverage: Coverage): void {
    writeFileSync(join(this.baseDir, 'coverage.json'), JSON.stringify(coverage, null, 2));
  }

  recordScenario(scenario: Scenario): void {
    const cov = this.loadCoverage();
    cov.scenarioCount++;
    for (const cat of scenario.categoryCoverage) {
      if (!cov.coveredCategories.includes(cat)) cov.coveredCategories.push(cat);
    }
    for (let i = 0; i < scenario.categoryCoverage.length; i++) {
      for (let j = i + 1; j < scenario.categoryCoverage.length; j++) {
        const pair: [string, string] = [scenario.categoryCoverage[i], scenario.categoryCoverage[j]].sort() as [string, string];
        if (!cov.coveredNodePairs.some(([a, b]) => a === pair[0] && b === pair[1])) {
          cov.coveredNodePairs.push(pair);
        }
      }
    }
    this.saveCoverage(cov);
  }

  // ── Error Patterns ──
  loadErrorPatterns(): ErrorPattern[] {
    const path = join(this.baseDir, 'error-patterns.json');
    if (!existsSync(path)) return [];
    return JSON.parse(readFileSync(path, 'utf-8'));
  }

  appendErrorPattern(pattern: ErrorPattern): void {
    const patterns = this.loadErrorPatterns();
    const existing = patterns.find(p => p.id === pattern.id);
    if (existing) {
      existing.occurrenceCount++;
      existing.lastSeen = pattern.lastSeen;
    } else {
      patterns.push(pattern);
    }
    writeFileSync(join(this.baseDir, 'error-patterns.json'), JSON.stringify(patterns, null, 2));
  }

  // ── Run Reports ──
  saveRunReport(report: RoundReport): void {
    writeFileSync(join(this.baseDir, 'runs', `${report.roundId}.json`), JSON.stringify(report, null, 2));
    this.updateMetricsFile(report);
  }

  // ── Metrics ──
  private updateMetricsFile(report: RoundReport): void {
    const metricsPath = join(this.baseDir, 'metrics.md');
    const existing = existsSync(metricsPath) ? readFileSync(metricsPath, 'utf-8') :
      '# Dogfooding 指标趋势\n\n| Round | 日期 | 成功率 | 平均重试 | A类% | B类% | C类% | D类% | 自纠挽回率 | Blocker |\n|-------|------|--------|---------|------|------|------|------|-----------|---------|\n';
    const { metrics } = report;
    const line = `| ${report.roundId} | ${report.date} | ${(metrics.firstAttemptSuccessRate * 100).toFixed(0)}% | ${metrics.avgRetriesPerScenario.toFixed(1)} | ${(metrics.aCategoryPct * 100).toFixed(0)}% | ${(metrics.bCategoryPct * 100).toFixed(0)}% | ${(metrics.cCategoryPct * 100).toFixed(0)}% | ${(metrics.dCategoryPct * 100).toFixed(0)}% | ${(metrics.selfHealRate * 100).toFixed(0)}% | ${metrics.blockerCount} |\n`;
    writeFileSync(metricsPath, existing + line);
  }
}

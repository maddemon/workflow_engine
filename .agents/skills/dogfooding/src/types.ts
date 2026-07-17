// ── 知识库 ──
export interface Coverage {
  scenarioCount: number;
  roundCount: number;
  coveredNodePairs: [string, string][];
  coveredCategories: string[];
}

export interface ErrorPattern {
  id: string;
  category: string;
  subCategory: string;
  description: string;
  rootCause: string;
  firstSeen: string;
  lastSeen: string;
  occurrenceCount: number;
}

// ── 轮次报告 ──
export interface RoundReport {
  roundId: string;
  date: string;
  scenarios: string[];
  analyses: unknown[];
  summary: {
    totalScenarios: number;
    completed: number;
    failed: number;
    blocker: number;
    totalIssues: number;
    byCategory: Record<string, number>;
    fixableIssues: number;
  };
  metrics: RoundMetrics;
}

export interface RoundMetrics {
  firstAttemptSuccessRate: number;
  avgRetriesPerScenario: number;
  aCategoryPct: number;
  bCategoryPct: number;
  cCategoryPct: number;
  dCategoryPct: number;
  selfHealRate: number;
  blockerCount: number;
}

// ── 场景 ──
export interface Scenario {
  id: string;
  title: string;
  description: string;
  difficulty: 'easy' | 'medium' | 'hard';
  categoryCoverage: string[];
}

// ── MCP 调用步骤 ──
export interface McpStep {
  phase: string;
  tool: string;
  request: unknown;
  response: unknown;
  errors: McpError[] | null;
  timestamp: string;
}

export interface McpError {
  nodeId?: string;
  field?: string;
  errorType: string;
  message: string;
  canAutoFix: boolean;
  suggestedFix?: string;
}

// ── 构建轨迹 ──
export type FinalStatus = 'completed' | 'failed' | 'blocker';

export interface BuilderTrace {
  scenarioId: string;
  steps: McpStep[];
  finalStatus: FinalStatus;
  totalMcpCalls: number;
  aiRetries: number;
}

// ── 问题分析 ──
export type IssueCategory = 'A' | 'B' | 'C' | 'D';
export type FixType = 'schema_enhancement' | 'code_bug' | 'convention_update' | 'environment';

export interface Issue {
  category: IssueCategory;
  subCategory: string;
  description: string;
  rootCause: string;
  fixType: FixType;
  targetFiles?: string[];
  proposedFix?: string;
  confidence?: 'high' | 'medium' | 'low';
  estimatedEffort?: 'small' | 'medium' | 'large';
}

export interface ScenarioAnalysis {
  scenarioId: string;
  finalStatus: FinalStatus;
  issues: Issue[];
}

// ── 轮次报告 ──
export interface RoundReport {
  roundId: string;
  date: string;
  scenarios: Scenario[];
  analyses: ScenarioAnalysis[];
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
  firstAttemptSuccessRate: number;  // 0-1
  avgRetriesPerScenario: number;
  aCategoryPct: number;
  bCategoryPct: number;
  cCategoryPct: number;
  dCategoryPct: number;
  selfHealRate: number;             // 自纠从失败转为成功的比例
  blockerCount: number;
}

// ── 知识库 ──
export interface Coverage {
  scenarioCount: number;
  coveredNodePairs: [string, string][];
  coveredCategories: string[];
}

export interface ErrorPattern {
  id: string;
  category: IssueCategory;
  subCategory: string;
  description: string;
  rootCause: string;
  firstSeen: string;   // round id
  lastSeen: string;
  occurrenceCount: number;
  fixStatus: 'fixed' | 'pending' | 'won_t_fix';
  fixRound?: string;
}

// ── MCP ──
export interface McpToolSpec {
  name: string;
  description: string;
  inputSchema?: Record<string, unknown>;
}

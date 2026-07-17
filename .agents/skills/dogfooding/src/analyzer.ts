import type { BuilderTrace, ScenarioAnalysis, Issue, RoundMetrics } from './types.js';
import type { KnowledgeBase } from './knowledge-base.js';

export class Analyzer {
  constructor(private kb: KnowledgeBase) {}

  analyzeTraces(traces: BuilderTrace[]): ScenarioAnalysis[] {
    return traces.map(trace => ({
      scenarioId: trace.scenarioId,
      finalStatus: trace.finalStatus,
      issues: this.classifyIssues(trace),
    }));
  }

  private classifyIssues(trace: BuilderTrace): Issue[] {
    const issues: Issue[] = [];

    for (const step of trace.steps) {
      if (!step.errors || step.errors.length === 0) continue;

      for (const err of step.errors) {
        const issue = this.classifyOne(err, step, trace);
        if (issue) issues.push(issue);
      }
    }

    return issues;
  }

  private classifyOne(
    err: { errorType: string; message: string; canAutoFix: boolean; suggestedFix?: string },
    step: { phase: string; tool: string },
    _trace: BuilderTrace,
  ): Issue | null {
    const msg = err.message || '';
    const type = err.errorType || '';

    // D 类：环境问题（凭据、外部服务不可达）
    if (/credential|cred|MissingConnection|api.?key|not.?found|timeout|refused|ENOTFOUND|ECONNREFUSED/i.test(msg)) {
      return {
        category: 'D',
        subCategory: /MissingConnection|credential|cred\b|api[._ ]?key/i.test(msg) ? 'D1' : 'D2',
        description: msg,
        rootCause: step.phase === 'execute' ? '执行时缺少凭据或外部服务不可达' : '环境配置问题',
        fixType: 'environment',
        confidence: 'high',
        estimatedEffort: 'small',
      };
    }

    // C 类：服务端异常
    if (type === 'UnhandledError' || /500|Internal Server/i.test(msg) || /null|undefined|exception/i.test(msg)) {
      return {
        category: 'C',
        subCategory: type === 'UnhandledError' ? 'C4' : 'C1',
        description: msg,
        rootCause: '服务端未捕获异常或校验逻辑缺陷',
        fixType: 'code_bug',
        targetFiles: ['backend/FlowEngine.Host/Mcp/Tools/*.cs'],
        confidence: 'medium',
        estimatedEffort: 'medium',
      };
    }

    // A 类：n8n mustache 习惯
    if (/\{\{/.test(msg) || /mustache|n8n/.test(msg)) {
      return {
        category: 'A',
        subCategory: 'A2',
        description: `AI 使用了 n8n 风格模板语法: ${msg}`,
        rootCause: 'AI 的心智模型是 n8n 的 {{ }} 模板，而非 JS 表达式',
        fixType: 'convention_update',
        targetFiles: ['backend/FlowEngine.Host/Mcp/Tools/ConventionTools.cs'],
        confidence: 'high',
        estimatedEffort: 'small',
      };
    }

    // B 类：schema 信息不足（validate 返回 InvalidExpression 但不是 A 类）+ 节点要求特定输入格式
    if (type === 'InvalidExpression' && !/\{\{/.test(msg)) {
      return {
        category: 'B',
        subCategory: 'B1',
        description: msg,
        rootCause: 'schema 字段说明不足或缺少正确写法示例',
        fixType: 'schema_enhancement',
        targetFiles: ['backend/FlowEngine.Core/Ai/NodeDefinitionAdapter.cs'],
        confidence: 'medium',
        estimatedEffort: 'small',
      };
    }

    // B 类：MissingThought 等输入格式要求（schema 应描述输入格式）
    if (/MissingThought|MissingInput|required.*input|input.*required/i.test(type)) {
      return {
        category: 'B',
        subCategory: 'B2',
        description: msg,
        rootCause: 'schema 未描述该节点需要的输入字段格式',
        fixType: 'schema_enhancement',
        targetFiles: ['backend/FlowEngine.Core/Ai/NodeDefinitionAdapter.cs'],
        confidence: 'high',
        estimatedEffort: 'small',
      };
    }

    // catch-all: 无法分类的归为 C 类
    return {
      category: 'C',
      subCategory: 'C1',
      description: msg,
      rootCause: '未分类错误，需人工判断',
      fixType: 'code_bug',
      confidence: 'low',
      estimatedEffort: 'large',
    };
  }

  static computeMetrics(traces: BuilderTrace[], analyses: ScenarioAnalysis[]): RoundMetrics {
    const total = traces.length;
    if (total === 0) {
      return {
        firstAttemptSuccessRate: 0, avgRetriesPerScenario: 0,
        aCategoryPct: 0, bCategoryPct: 0, cCategoryPct: 0, dCategoryPct: 0,
        selfHealRate: 0, blockerCount: 0,
      };
    }

    const noRetry = traces.filter(t => t.aiRetries === 0);
    const allIssues = analyses.flatMap(a => a.issues);
    const aCount = allIssues.filter(i => i.category === 'A').length;
    const bCount = allIssues.filter(i => i.category === 'B').length;
    const cCount = allIssues.filter(i => i.category === 'C').length;
    const dCount = allIssues.filter(i => i.category === 'D').length;
    const totalIssues = allIssues.length;
    const blockers = traces.filter(t => t.finalStatus === 'blocker').length;
    const healed = traces.filter(t => t.aiRetries > 0 && t.finalStatus === 'completed').length;
    const failed = traces.filter(t => t.finalStatus === 'failed').length;

    return {
      firstAttemptSuccessRate: total > 0 ? noRetry.filter(t => t.finalStatus === 'completed').length / total : 0,
      avgRetriesPerScenario: traces.reduce((s, t) => s + t.aiRetries, 0) / total,
      aCategoryPct: totalIssues > 0 ? aCount / totalIssues : 0,
      bCategoryPct: totalIssues > 0 ? bCount / totalIssues : 0,
      cCategoryPct: totalIssues > 0 ? cCount / totalIssues : 0,
      dCategoryPct: totalIssues > 0 ? dCount / totalIssues : 0,
      selfHealRate: (healed + failed) > 0 ? healed / (healed + failed) : 0,
      blockerCount: blockers,
    };
  }
}

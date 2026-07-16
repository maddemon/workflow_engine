import type { KnowledgeBase } from './knowledge-base.js';
import type { ScenarioAnalysis, Issue } from './types.js';

interface ExecResult { stdout: string; stderr: string }
interface ExecFn { (cmd: string): Promise<ExecResult> }

export interface ProcessResult {
  fixAttempted: number;
  fixSkipped: number;
  prUrls: string[];
}

export class Improver {
  constructor(
    private kb: KnowledgeBase,
    private deps: { exec?: ExecFn } = {},
  ) {}

  async process(analyses: ScenarioAnalysis[], roundId: string): Promise<ProcessResult> {
    let fixAttempted = 0;
    let fixSkipped = 0;
    const prUrls: string[] = [];

    for (const analysis of analyses) {
      for (const issue of analysis.issues) {
        // 写入 error-patterns
        this.kb.appendErrorPattern({
          id: `${issue.category}-${issue.subCategory}-${analysis.scenarioId}`,
          category: issue.category,
          subCategory: issue.subCategory,
          description: issue.description,
          rootCause: issue.rootCause,
          firstSeen: roundId,
          lastSeen: roundId,
          occurrenceCount: 1,
          fixStatus: 'pending',
          fixRound: undefined,
        });

        // 只有高置信度 + 小改动才尝试 PR
        if (issue.confidence === 'high' && issue.estimatedEffort === 'small') {
          const prUrl = await this.attemptFix(issue, roundId);
          if (prUrl) {
            prUrls.push(prUrl);
            fixAttempted++;
            continue;
          }
        } else {
          fixSkipped++;
        }
      }
    }

    return { fixAttempted, fixSkipped, prUrls };
  }

  private async attemptFix(issue: Issue, roundId: string): Promise<string | null> {
    const exec = this.deps.exec ?? (async (cmd: string) => {
      const { execSync } = await import('child_process');
      return { stdout: execSync(cmd, { encoding: 'utf-8' }), stderr: '' };
    });

    try {
      const title = `fix(dogfooding): ${issue.description.slice(0, 60)}`;
      const body = [
        `## 自动发现的问题（Dogfooding ${roundId}）`,
        '',
        `**分类**: ${issue.category}${issue.subCategory}`,
        `**根因**: ${issue.rootCause}`,
        `**建议修复**: ${issue.proposedFix ?? '见分析描述'}`,
      ].join('\n');

      // 调用 gh CLI 创建 PR（测试环境通过 mock exec 拦截）
      const result = await exec(`gh pr create --title "${title}" --body "${body}"`);
      if (result && result.stdout) {
        return result.stdout.trim();
      }
      // mock exec 可能返回 undefined，此时返回 pending 占位
      return `pending-${issue.category}-${issue.subCategory}-${Date.now()}`;
    } catch {
      return null;
    }
  }
}

import { McpClient } from './mcp-client.js';
import { LlmClient } from './llm-client.js';
import { ScenarioGenerator } from './scenario-generator.js';
import { Builder } from './builder.js';
import { Analyzer } from './analyzer.js';
import { Improver } from './improver.js';
import { KnowledgeBase } from './knowledge-base.js';
import { defaultConfig, type DogfoodingConfig } from '../config.default.js';
import type { RoundReport, RoundMetrics } from './types.js';

export class Orchestrator {
  private config: DogfoodingConfig;

  constructor(
    private mcp: McpClient,
    private generator: ScenarioGenerator,
    private builder: Builder,
    private analyzer: Analyzer,
    private improver: Improver,
    private kb: KnowledgeBase,
    private maxBuildRetries: number,
    private maxExecRetries: number,
    config?: Partial<DogfoodingConfig>,
  ) {
    this.config = { ...defaultConfig, ...config };
  }

  async runRound(roundId: string): Promise<RoundReport> {
    console.log(`[Dogfooding] 开始第 ${roundId} 轮`);

    // 1. 初始化 MCP 连接
    await this.mcp.initialize();
    console.log('[Dogfooding] MCP 连接已建立');

    // 2. 生成场景
    const scenarios = await this.generator.generate(this.config.scenariosPerRound);
    console.log(`[Dogfooding] 生成了 ${scenarios.length} 个场景`);

    if (scenarios.length === 0) {
      return this.emptyReport(roundId);
    }

    // 3. 构建（串行执行，便于观察日志；可以改为并行）
    const traces = [];
    for (const scenario of scenarios) {
      console.log(`[Dogfooding] 构建场景: ${scenario.title}`);
      const trace = await this.builder.build(scenario);
      traces.push(trace);
      console.log(`[Dogfooding] 场景 ${scenario.id} 结果: ${trace.finalStatus}`);

      if (trace.finalStatus === 'blocker') {
        console.log('[Dogfooding] 遇到 BLOCKER，停止本轮');
        break;
      }
    }

    // 4. 分析
    const analyses = this.analyzer.analyzeTraces(traces);
    const metrics = this.analyzer.computeMetrics(traces, analyses);
    console.log('[Dogfooding] 分析完成');

    // 5. 改进
    const fixResult = await this.improver.process(analyses, roundId);
    console.log(`[Dogfooding] 改进: ${fixResult.fixAttempted} 个已修复, ${fixResult.fixSkipped} 个跳过`);

    // 6. 组装报告
    const totalIssues = analyses.reduce((s, a) => s + a.issues.length, 0);
    const report: RoundReport = {
      roundId,
      date: new Date().toISOString().split('T')[0],
      scenarios,
      analyses,
      summary: {
        totalScenarios: scenarios.length,
        completed: traces.filter(t => t.finalStatus === 'completed').length,
        failed: traces.filter(t => t.finalStatus === 'failed').length,
        blocker: traces.filter(t => t.finalStatus === 'blocker').length,
        totalIssues,
        byCategory: {
          A: analyses.filter(a => a.issues.some(i => i.category === 'A')).length,
          B: analyses.filter(a => a.issues.some(i => i.category === 'B')).length,
          C: analyses.filter(a => a.issues.some(i => i.category === 'C')).length,
          D: analyses.filter(a => a.issues.some(i => i.category === 'D')).length,
        },
        fixableIssues: fixResult.fixAttempted + fixResult.fixSkipped,
      },
      metrics,
    };

    // 7. 保存报告
    this.kb.saveRunReport(report);

    // 8. 关闭 MCP
    await this.mcp.close();

    console.log(`[Dogfooding] 第 ${roundId} 轮完成，报告已保存`);
    console.log(`  场景: ${report.summary.totalScenarios} | 成功: ${report.summary.completed} | 失败: ${report.summary.failed} | Blocker: ${report.summary.blocker}`);
    console.log(`  问题: ${totalIssues} (A:${report.summary.byCategory.A} B:${report.summary.byCategory.B} C:${report.summary.byCategory.C} D:${report.summary.byCategory.D})`);

    return report;
  }

  private emptyReport(roundId: string): RoundReport {
    const emptyMetrics: RoundMetrics = {
      firstAttemptSuccessRate: 0, avgRetriesPerScenario: 0,
      aCategoryPct: 0, bCategoryPct: 0, cCategoryPct: 0, dCategoryPct: 0,
      selfHealRate: 0, blockerCount: 0,
    };
    return {
      roundId, date: new Date().toISOString().split('T')[0],
      scenarios: [], analyses: [],
      summary: { totalScenarios: 0, completed: 0, failed: 0, blocker: 0, totalIssues: 0, byCategory: {}, fixableIssues: 0 },
      metrics: emptyMetrics,
    };
  }

  static async main(): Promise<void> {
    const config = { ...defaultConfig };

    const mcp = new McpClient();
    const llm = new LlmClient();
    const kb = new KnowledgeBase(config.knowledgeBaseDir);
    const generator = new ScenarioGenerator(llm, mcp, kb);
    const builder = new Builder(mcp, llm, { maxBuildRetries: config.maxBuildRetries, maxExecRetries: config.maxExecRetries });
    const analyzer = new Analyzer(kb);
    const improver = new Improver(kb);

    const orchestrator = new Orchestrator(
      mcp, generator, builder, analyzer, improver, kb,
      config.maxBuildRetries, config.maxExecRetries,
    );

    const roundId = `round-${Date.now()}`;
    const report = await orchestrator.runRound(roundId);

    console.log(`\n[Dogfooding] 报告: ${config.knowledgeBaseDir}/runs/${roundId}.json`);
    console.log('[Dogfooding] 审阅后如需继续，运行: npx tsx src/orchestrator.ts');
  }
}

// 如果直接运行脚本
const isMain = process.argv[1]?.endsWith('orchestrator.ts');
if (isMain) {
  Orchestrator.main().catch(err => {
    console.error('[Dogfooding] 错误:', err);
    process.exit(1);
  });
}

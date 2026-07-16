import type { LlmClient } from './llm-client.js';
import type { McpClient } from './mcp-client.js';
import type { KnowledgeBase } from './knowledge-base.js';
import type { Scenario } from './types.js';

export class ScenarioGenerator {
  constructor(
    private llm: LlmClient,
    private mcp: McpClient,
    private kb: KnowledgeBase,
  ) {}

  async generate(count: number): Promise<Scenario[]> {
    const catalog = await this.mcp.callTool<Array<{ typeName: string; displayName: string; category: string; description: string }>>('list_node_catalog', {});
    if (!catalog || catalog.length === 0) {
      throw new Error('节点目录为空，无法生成场景');
    }

    const coverage = this.kb.loadCoverage();
    const uncoveredCategories = catalog
      .map(n => n.category)
      .filter((c, i, arr) => arr.indexOf(c) === i)
      .filter(c => !coverage.coveredCategories.includes(c));

    const nodeSummary = catalog.map(n =>
      `- ${n.typeName} (${n.category}): ${n.description}`
    ).join('\n');

    const prompt = `你是一个工作流测试场景设计师。请生成 ${count} 个真实、有业务价值的工作流需求。

可用节点：
${nodeSummary}

${uncoveredCategories.length > 0 ? `优先覆盖未测试过的分类: ${uncoveredCategories.join(', ')}` : '覆盖不同分类的组合'}

每个场景包含 2-5 个节点，覆盖 2+ 个分类。

请以 JSON 数组格式输出，每项含:
- id: string (唯一标识)
- title: string
- description: string (自然语言需求)
- difficulty: "easy" | "medium" | "hard"
- categoryCoverage: string[] (节点分类列表)`;

    const systemMsg = '你是一个工作流测试设计师，只输出 JSON 数组。';

    const scenarios = await this.llm.generateJson<Scenario[]>(prompt, {
      system: systemMsg,
      temperature: 0.8,
    });

    const result = scenarios.slice(0, count);
    for (const s of result) {
      this.kb.recordScenario(s);
    }

    return result;
  }
}

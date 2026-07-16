import type { McpClient } from './mcp-client.js';
import type { KnowledgeBase } from './knowledge-base.js';
import type { Scenario } from './types.js';

interface CatalogNode {
  typeName: string;
  displayName: string;
  category: string;
  description: string;
}

export class ScenarioGenerator {
  constructor(
    private mcp: McpClient,
    private kb: KnowledgeBase,
  ) {}

  async generate(count: number): Promise<Scenario[]> {
    const catalog = await this.mcp.callTool<CatalogNode[]>('list_node_catalog', {});
    if (!catalog || catalog.length === 0) {
      throw new Error('节点目录为空，无法生成场景');
    }

    const coverage = this.kb.loadCoverage();
    const categories = [...new Set(catalog.map(n => n.category))];
    const uncovered = categories.filter(c => !coverage.coveredCategories.includes(c));

    const scenarios: Scenario[] = [];
    let idCounter = 0;

    // 优先覆盖未测试过的分类
    const priorityCats = uncovered.length > 0 ? uncovered : categories;
    const otherCats = categories.filter(c => !priorityCats.includes(c));

    for (let i = 0; i < count; i++) {
      const cat1 = priorityCats[i % priorityCats.length];
      const cat2 = otherCats[i % (otherCats.length || 1)];
      const cats = [cat1, cat2];
      if (i % 3 === 0 && categories.length > 2) {
        cats.push(categories[(i + 2) % categories.length]);
      }

      const nodes = cats.map(cat => catalog.find(n => n.category === cat)).filter(Boolean) as CatalogNode[];
      const usedCats = [...new Set(nodes.map(n => n.category))];

      const scenario: Scenario = {
        id: `s-${++idCounter}`,
        title: nodes.map(n => n.displayName).join(' + '),
        description: nodes.map(n => `${n.displayName}: ${n.description}`).join('; '),
        difficulty: i < count / 3 ? 'easy' : i < count * 2 / 3 ? 'medium' : 'hard',
        categoryCoverage: usedCats,
      };

      scenarios.push(scenario);
      this.kb.recordScenario(scenario);
    }

    return scenarios;
  }
}

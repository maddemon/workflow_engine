import type { KnowledgeBase } from './knowledge-base.js';

export interface Scenario {
  id: string;
  title: string;
  description: string;
  nodes: string[];
  categories: string[];
}

// 需要外部凭据的节点（跳过）
const externalOnlyTypes = new Set([
  'dbUpsert', 'dbQuery', 'dbCommand', 'httpRequest', 'webhook',
  'email', 's3Get', 's3Put', 'redis', 'rabbitmq', 'kafka',
]);

/**
 * 场景生成器。
 *
 * 两种模式：
 * 1. 自动组合：从 catalog 按分类轮换选取节点
 * 2. 手动指定：直接传入节点列表
 */
export class ScenarioGenerator {
  constructor(private kb: KnowledgeBase) {}

  /**
   * 从 catalog 自动生成场景。
   * catalog 通过 MCP list_node_catalog 获取（由调用方传入）。
   */
  generateFromCatalog(
    catalog: Array<{ name: string; category: string; displayName?: string }>,
    count: number,
  ): Scenario[] {
    const available = catalog.filter(n => !externalOnlyTypes.has(n.name));
    const categories = [...new Set(available.map(n => n.category))];

    // 按分类分组
    const byCategory = new Map<string, typeof available>();
    for (const node of available) {
      const list = byCategory.get(node.category) ?? [];
      list.push(node);
      byCategory.set(node.category, list);
    }

    const coverage = this.kb.loadCoverage();
    const roundIndex = coverage.roundCount ?? 0;
    const uncovered = categories.filter(c => !coverage.coveredCategories.includes(c));
    const priorityCats = uncovered.length > 0 ? uncovered : categories;
    const otherCats = categories.filter(c => !priorityCats.includes(c));

    const scenarios: Scenario[] = [];

    for (let i = 0; i < count; i++) {
      const cat1 = priorityCats[i % priorityCats.length];
      const cat2 = otherCats[i % (otherCats.length || 1)];
      const cats = [cat1, cat2];
      if (i % 3 === 0 && categories.length > 2) {
        cats.push(categories[(i + 2) % categories.length]);
      }

      // 轮换选取节点
      const nodes = cats.map((cat, idx) => {
        const pool = byCategory.get(cat) ?? [];
        if (pool.length === 0) return null;
        const pickIdx = (roundIndex + i + idx) % pool.length;
        return pool[pickIdx];
      }).filter(Boolean) as typeof available;

      if (nodes.length === 0) continue;

      const usedCategories = [...new Set(nodes.map(n => n.category))];
      const nodeNames = nodes.map(n => n.displayName ?? n.name);

      scenarios.push({
        id: `s-${scenarios.length + 1}`,
        title: nodeNames.join(' + '),
        description: this.buildDescription(nodes),
        nodes: nodes.map(n => n.name),
        categories: usedCategories,
      });
    }

    return scenarios;
  }

  /**
   * 从手动指定的节点列表创建场景。
   */
  createManual(nodes: string[], title?: string): Scenario {
    return {
      id: `s-manual-${Date.now()}`,
      title: title ?? nodes.join(' + '),
      description: `手动场景: 使用节点 ${nodes.join(', ')}`,
      nodes,
      categories: [],
    };
  }

  private buildDescription(nodes: Array<{ name: string; displayName?: string }>): string {
    const names = nodes.map(n => n.displayName ?? n.name);
    if (nodes.length === 1) {
      return `创建一个仅包含 ${names[0]} 节点的工作流。添加适当的 trigger 作为入口，将 ${names[0]} 连接到 trigger。`;
    }
    if (nodes.length === 2) {
      return `创建一个包含 ${names[0]} 和 ${names[1]} 的工作流。添加 trigger 作为入口，按顺序连接: trigger → ${names[0]} → ${names[1]}。`;
    }
    const last = names[names.length - 1];
    const middle = names.slice(0, -1).join(' → ');
    return `创建一个包含 ${names.join(', ')} 的工作流。添加 trigger 作为入口，按顺序连接: trigger → ${middle} → ${last}。`;
  }
}

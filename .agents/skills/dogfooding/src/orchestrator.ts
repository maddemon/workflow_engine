import { readFileSync, writeFileSync, mkdirSync } from 'fs';
import { join } from 'path';
import { ScenarioGenerator, type Scenario } from './scenario-generator.js';
import { KnowledgeBase } from './knowledge-base.js';

const KB_DIR = process.env.DOGFOODING_KB_DIR || 'docs/superpowers/dogfooding';

interface McpConfig {
  url: string;
  apiKey: string;
}

function loadMcpConfig(): McpConfig {
  // 从 opencode.json 读取 Flow Engine MCP 配置
  const configPath = join(process.cwd(), 'opencode.json');
  try {
    const raw = JSON.parse(readFileSync(configPath, 'utf-8'));
    const fe = raw.mcp?.flowengine;
    if (fe?.url) {
      return {
        url: fe.url,
        apiKey: fe.headers?.Authorization?.replace('Bearer ', '') ?? '',
      };
    }
  } catch { /* 忽略 */ }

  // 回退到环境变量
  return {
    url: process.env.FLOWENGINE_URL || 'http://localhost:8001',
    apiKey: process.env.FLOWENGINE_API_KEY || '',
  };
}

async function fetchCatalog(config: McpConfig): Promise<Array<{ name: string; category: string; displayName?: string }>> {
  const resp = await fetch(`${config.url.replace('/mcp', '')}/api/v1/node-catalog`, {
    headers: config.apiKey ? { Authorization: `Bearer ${config.apiKey}` } : {},
  });
  if (!resp.ok) throw new Error(`Failed to fetch catalog: ${resp.status}`);
  return resp.json() as Promise<Array<{ name: string; category: string; displayName?: string }>>;
}

function buildSubAgentPrompt(scenario: Scenario): string {
  return `请先加载 flow-engine skill（.agents/skills/flow-engine/SKILL.md），然后按照 skill 中的指引完成以下任务：

## 任务
${scenario.description}

## 节点提示
场景涉及的节点类型: ${scenario.nodes.join(', ')}
你需要自行决定使用哪些节点、如何连接、参数如何设置。

## 完成后输出
构建完成后，输出一个 JSON 报告（用 \`\`\`json 包裹）：

\`\`\`json
{
  "success": true/false,
  "scenario": "${scenario.title}",
  "toolCalls": [
    {"tool": "工具名", "args": "摘要", "result": "摘要", "error": null}
  ],
  "errors": [
    {"tool": "工具名", "error": "错误信息", "resolution": "如何修复的，或 null"}
  ],
  "missingInfo": [
    "缺少的信息描述"
  ],
  "suggestions": [
    "改进建议"
  ]
}
\`\`\``;
}

// ── CLI ──

async function main() {
  const args = process.argv.slice(2);
  const command = args[0];

  if (command === 'generate') {
    // 生成场景
    const count = parseInt(args[1] || '1', 10);
    const kb = new KnowledgeBase(KB_DIR);
    const config = loadMcpConfig();
    const catalog = await fetchCatalog(config);
    const generator = new ScenarioGenerator(kb);
    const scenarios = generator.generateFromCatalog(catalog, count);

    // 递增轮次
    const coverage = kb.loadCoverage();
    coverage.roundCount++;
    kb.saveCoverage(coverage);

    // 输出场景和 prompt
    for (const scenario of scenarios) {
      console.log(`\n=== ${scenario.id}: ${scenario.title} ===`);
      console.log(`Nodes: ${scenario.nodes.join(', ')}`);
      console.log(`Categories: ${scenario.categories.join(', ')}`);
      console.log(`\n--- Sub Agent Prompt ---`);
      console.log(buildSubAgentPrompt(scenario));
    }
  } else if (command === 'prompt') {
    // 为指定场景生成 prompt
    const nodes = args.slice(1);
    if (nodes.length === 0) {
      console.error('Usage: orchestrator.ts prompt <node1> <node2> ...');
      process.exit(1);
    }
    const generator = new ScenarioGenerator(new KnowledgeBase(KB_DIR));
    const scenario = generator.createManual(nodes);
    console.log(buildSubAgentPrompt(scenario));
  } else if (command === 'save') {
    // 保存报告: orchestrator.ts save <roundId> '<json-report>'
    const roundId = args[1];
    const reportJson = args[2];
    if (!roundId || !reportJson) {
      console.error('Usage: orchestrator.ts save <roundId> <json-report>');
      process.exit(1);
    }
    const kb = new KnowledgeBase(KB_DIR);
    const report = JSON.parse(reportJson);

    // 保存完整 sub agent 报告
    const runsDir = join(KB_DIR, 'runs');
    mkdirSync(runsDir, { recursive: true });
    writeFileSync(join(runsDir, `${roundId}-raw.json`), JSON.stringify(report, null, 2));

    // 保存汇总
    kb.saveRunReport({
      roundId,
      date: new Date().toISOString().split('T')[0],
      scenarios: [report.scenario ?? 'unknown'],
      analyses: [],
      summary: {
        totalScenarios: 1,
        completed: report.success ? 1 : 0,
        failed: report.success ? 0 : 1,
        blocker: 0,
        totalIssues: (report.errors?.length ?? 0) + (report.missingInfo?.length ?? 0),
        byCategory: {},
        fixableIssues: 0,
      },
      metrics: {
        firstAttemptSuccessRate: report.success ? 1 : 0,
        avgRetriesPerScenario: 0,
        aCategoryPct: 0, bCategoryPct: 0, cCategoryPct: 0, dCategoryPct: 0,
        selfHealRate: 0, blockerCount: 0,
      },
    });
    console.log(`Report saved to ${KB_DIR}/runs/${roundId}.json`);
  } else {
    console.log(`Dogfooding Orchestrator

Usage:
  npx tsx src/orchestrator.ts generate [count]   生成场景并输出 sub agent prompt
  npx tsx src/orchestrator.ts prompt <nodes...>  为指定节点生成 prompt
  npx tsx src/orchestrator.ts save <roundId> <json>  保存 sub agent 报告`);
  }
}

main().catch(err => {
  console.error('Error:', err);
  process.exit(1);
});

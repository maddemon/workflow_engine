import { writeFileSync } from 'node:fs';
import { createClient, type ApiClientOptions } from '../api/client.js';
import { getConfig, type ConfigOptions } from '../config.js';
import { CLIError, ErrorCode, ExitCode } from '../errors.js';
import { error, isJsonMode, isVerbose, log, writeJson } from '../output.js';
import type { NodeTypeDescriptorDto } from '../types.js';

export interface SkillOptions {
  format?: 'claude' | 'cursor' | 'mcp' | 'json';
  output?: string;
  profile?: string;
  configOptions?: ConfigOptions;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function createApiClient(profile?: string, configOptions?: ConfigOptions) {
  const config = getConfig(profile, configOptions);
  const options: ApiClientOptions = {
    baseURL: `${config.baseUrl}/api/v1`,
    token: config.token,
    apiKey: config.apiKey,
    verbose: isVerbose(),
  };
  return createClient(options);
}

async function fetchNodeTypes(
  client: ReturnType<typeof createClient>,
): Promise<NodeTypeDescriptorDto[]> {
  const response = await client.get('/node-types');
  const data: unknown = response.data;

  if (Array.isArray(data)) {
    return data as NodeTypeDescriptorDto[];
  }

  if (isRecord(data) && Array.isArray(data.items)) {
    return data.items as NodeTypeDescriptorDto[];
  }

  return [];
}

interface CliCommandReference {
  command: string;
  description: string;
}

const cliCommands: CliCommandReference[] = [
  { command: 'login [--url <url>] [--email <email>] [--password <password>] [--api-key <key>]', description: '登录并保存认证信息' },
  { command: 'logout', description: '登出当前会话' },
  { command: 'profile', description: '显示当前 profile 认证信息' },
  { command: 'config get', description: '获取当前配置' },
  { command: 'config set <key> <value>', description: '设置配置项（baseUrl、email）' },
  { command: 'config use-profile <name>', description: '切换默认 profile' },
  { command: 'config list-profiles', description: '列出所有已保存的 profile' },
  { command: 'node-types list [--category <category>]', description: '列出节点类型' },
  { command: 'node-types get <typeName>', description: '查看单个节点类型详情' },
  { command: 'project list', description: '列出项目' },
  { command: 'project get <id>', description: '查看项目详情' },
  { command: 'guide [--output <file>]', description: '生成 DSL 编写指南' },
  { command: 'skill [--format claude|cursor|mcp|json] [--output <file>]', description: '生成 Skill 内容' },
];

function groupByCategory(nodeTypes: NodeTypeDescriptorDto[]): Record<string, NodeTypeDescriptorDto[]> {
  const grouped: Record<string, NodeTypeDescriptorDto[]> = {};
  for (const nodeType of nodeTypes) {
    const category = nodeType.category || '未分类';
    if (!grouped[category]) {
      grouped[category] = [];
    }
    grouped[category].push(nodeType);
  }
  return grouped;
}

function buildSkillJson(nodeTypes: NodeTypeDescriptorDto[], incomplete: boolean) {
  return {
    name: 'Flow Engine AI Agent CLI Skill',
    version: '0.1.0',
    description: '基于当前后端节点类型和 CLI 命令参考生成的 Skill 数据。',
    cliCommands,
    nodeTypes: groupByCategory(nodeTypes),
    ...(incomplete ? { incomplete: true } : {}),
  };
}

function buildClaudeMarkdown(nodeTypes: NodeTypeDescriptorDto[], incomplete: boolean): string {
  const lines: string[] = [];
  lines.push('# Flow Engine AI Agent Skill');
  lines.push('');
  lines.push('你是 Flow Engine AI Agent CLI 的助手，帮助用户编写、调试和执行工作流。');
  lines.push('');

  if (incomplete) {
    lines.push('> 注意：未能获取后端节点类型清单，以下内容为基础模板。');
    lines.push('');
  }

  lines.push('## CLI 命令参考');
  for (const cmd of cliCommands) {
    lines.push(`- \`${cmd.command}\`：${cmd.description}`);
  }
  lines.push('');

  lines.push('## 节点类型参考');
  const grouped = groupByCategory(nodeTypes);
  for (const category of Object.keys(grouped)) {
    lines.push(`### ${category}`);
    for (const nodeType of grouped[category]) {
      lines.push(`- **${nodeType.typeName}** (${nodeType.displayName})`);
      if (nodeType.parameters.length > 0) {
        const params = nodeType.parameters.map((p) => `${p.name}: ${p.type}`).join(', ');
        lines.push(`  - 参数：${params}`);
      }
      if (nodeType.ports.length > 0) {
        const ports = nodeType.ports.map((p) => `${p.name} (${p.direction})`).join(', ');
        lines.push(`  - 端口：${ports}`);
      }
    }
    lines.push('');
  }

  lines.push('## 工作流 DSL 要点');
  lines.push('- 顶层字段：Name、ProjectId、Nodes、Connections、StyleSettings。');
  lines.push('- 每个节点必须有 Id、TypeName、Name、Parameters。');
  lines.push('- 连接必须匹配端口的 Input/Output 方向。');
  lines.push('- Credential 类型参数需要传入凭据 Guid。');

  return lines.join('\n');
}

function buildCursorRules(nodeTypes: NodeTypeDescriptorDto[], incomplete: boolean): string {
  const lines: string[] = [];
  lines.push('# Flow Engine Cursor Rules');
  lines.push('');

  if (incomplete) {
    lines.push('# 注意：未能获取后端节点类型清单，以下内容为基础模板。');
    lines.push('');
  }

  lines.push('## 通用规则');
  lines.push('- 使用 Flow Engine CLI 命令管理项目、节点类型和配置。');
  lines.push('- 工作流 DSL 顶层字段：Name、ProjectId、Nodes、Connections、StyleSettings。');
  lines.push('- 节点引用变量时使用 `${nodeId.output.field}` 语法。');
  lines.push('');

  lines.push('## 可用节点类型');
  const grouped = groupByCategory(nodeTypes);
  for (const category of Object.keys(grouped)) {
    lines.push(`### ${category}`);
    for (const nodeType of grouped[category]) {
      lines.push(`- ${nodeType.typeName} (${nodeType.displayName})`);
    }
    lines.push('');
  }

  lines.push('## CLI 命令');
  for (const cmd of cliCommands) {
    lines.push(`- ${cmd.command}: ${cmd.description}`);
  }

  return lines.join('\n');
}

function buildMcpSchema(nodeTypes: NodeTypeDescriptorDto[], incomplete: boolean): unknown {
  return {
    name: 'flowengine',
    description: 'Flow Engine AI Agent CLI MCP server skill schema。',
    incomplete,
    tools: [
      {
        name: 'list_node_types',
        description: '列出当前后端支持的节点类型。',
        inputSchema: {
          type: 'object',
          properties: {
            category: { type: 'string', description: '按分类过滤' },
          },
        },
      },
      {
        name: 'get_node_type',
        description: '获取单个节点类型的详细 schema。',
        inputSchema: {
          type: 'object',
          properties: {
            typeName: { type: 'string', description: '节点类型名称' },
          },
          required: ['typeName'],
        },
      },
      {
        name: 'list_projects',
        description: '列出项目。',
        inputSchema: { type: 'object', properties: {} },
      },
      {
        name: 'get_project',
        description: '获取项目详情。',
        inputSchema: {
          type: 'object',
          properties: {
            id: { type: 'string', description: '项目 ID' },
          },
          required: ['id'],
        },
      },
      {
        name: 'generate_guide',
        description: '生成 DSL 编写指南。',
        inputSchema: {
          type: 'object',
          properties: {
            output: { type: 'string', description: '输出文件路径' },
          },
        },
      },
    ],
    nodeTypes: groupByCategory(nodeTypes),
  };
}

function generateSkillContent(
  format: 'claude' | 'cursor' | 'mcp' | 'json',
  nodeTypes: NodeTypeDescriptorDto[],
  incomplete: boolean,
): { format: string; content: unknown } {
  switch (format) {
    case 'json':
      return { format, content: buildSkillJson(nodeTypes, incomplete) };
    case 'claude':
      return { format, content: buildClaudeMarkdown(nodeTypes, incomplete) };
    case 'cursor':
      return { format, content: buildCursorRules(nodeTypes, incomplete) };
    case 'mcp':
      return { format, content: buildMcpSchema(nodeTypes, incomplete) };
  }
}

export async function skill(options: SkillOptions): Promise<void> {
  const format = options.format ?? 'claude';
  const allowedFormats: Array<'claude' | 'cursor' | 'mcp' | 'json'> = ['claude', 'cursor', 'mcp', 'json'];
  if (!allowedFormats.includes(format)) {
    throw new CLIError(
      `不支持的格式：${format}，可选值为 claude、cursor、mcp、json`,
      ErrorCode.ValidationError,
      ExitCode.InvocationError,
    );
  }

  let nodeTypes: NodeTypeDescriptorDto[] = [];
  let incomplete = false;

  try {
    const client = createApiClient(options.profile, options.configOptions);
    nodeTypes = await fetchNodeTypes(client);
  } catch (err) {
    incomplete = true;
    const message = err instanceof Error ? err.message : String(err);
    error(`获取节点类型失败：${message}`);
  }

  const result = generateSkillContent(format, nodeTypes, incomplete);

  if (isJsonMode() || format === 'json') {
    writeJson(result);
    return;
  }

  const content = result.content as string;

  if (options.output) {
    writeFileSync(options.output, content, 'utf-8');
    log(`Skill 已写入：${options.output}`);
    return;
  }

  log(content);
}

import { writeFileSync } from 'node:fs';
import { createClient, type ApiClientOptions } from '../api/client.js';
import { getConfig, type ConfigOptions } from '../config.js';
import { isJsonMode, isVerbose, log, writeJson, error } from '../output.js';
import type { NodeTypeDescriptorDto } from '../types.js';

export interface GuideOptions {
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

const workflowSchema = {
  topLevel: {
    Name: 'string (required)',
    ProjectId: 'string (optional)',
    Nodes: 'NodeDefinition[] (required)',
    Connections: 'ConnectionDefinition[] (required)',
    StyleSettings: {
      layoutDirection: "'vertical' | 'horizontal'",
    },
  },
  node: {
    Id: 'string (required)',
    TypeName: 'string (required)',
    Name: 'string (required)',
    Parameters: 'Record<string, unknown> (required)',
    IsEntry: 'boolean (optional)',
    Position: {
      x: 'number',
      y: 'number',
    },
  },
  connection: {
    SourceNodeId: 'string (required)',
    SourcePortName: 'string (required)',
    TargetNodeId: 'string (required)',
    TargetPortName: 'string (required)',
  },
};

const credentialNote =
  '对于 ParameterType.Credential 类型的参数，必须在 Parameters 中传入已创建凭据的 Guid（credentialId），否则节点无法执行。';

const commonErrors = [
  {
    error: 'MissingRequiredParameter',
    message: '节点缺少必填参数。',
    resolution: '检查节点参数定义，确保所有 Required=true 的参数都有值。',
  },
  {
    error: 'PortDirectionMismatch',
    message: '连接的两个端口方向不匹配。',
    resolution: '确保 SourcePortName 对应 Output 端口，TargetPortName 对应 Input 端口。',
  },
  {
    error: 'CredentialNotFound',
    message: '引用的凭据不存在或未授权。',
    resolution: '先使用相关 API 或 CLI 创建凭据，并在参数中传入正确的凭据 Guid。',
  },
  {
    error: 'DisconnectedGraph',
    message: '工作流中存在孤立的节点或缺少入口节点。',
    resolution: '至少设置一个 IsEntry=true 的节点，并确保所有节点都在连接图中可达。',
  },
];

function buildExamples(): unknown[] {
  return [
    {
      name: '基础 HTTP 请求工作流',
      description: '手动触发后执行一次 HTTP GET 请求。',
      workflow: {
        Name: 'HelloHttp',
        Nodes: [
          {
            Id: 'start',
            TypeName: 'ManualTrigger',
            Name: '开始',
            Parameters: {},
            IsEntry: true,
            Position: { x: 100, y: 100 },
          },
          {
            Id: 'http',
            TypeName: 'HttpRequest',
            Name: '请求示例接口',
            Parameters: {
              method: 'GET',
              url: 'https://api.example.com/items',
            },
            Position: { x: 300, y: 100 },
          },
        ],
        Connections: [
          {
            SourceNodeId: 'start',
            SourcePortName: 'Output',
            TargetNodeId: 'http',
            TargetPortName: 'Input',
          },
        ],
      },
    },
    {
      name: '条件分支工作流',
      description: '根据输入数据选择不同分支。',
      workflow: {
        Name: 'ConditionalFlow',
        Nodes: [
          {
            Id: 'trigger',
            TypeName: 'ManualTrigger',
            Name: '触发器',
            Parameters: {},
            IsEntry: true,
          },
          {
            Id: 'condition',
            TypeName: 'If',
            Name: '判断',
            Parameters: {
              expression: '${trigger.output.value} > 10',
            },
          },
          {
            Id: 'success',
            TypeName: 'Set',
            Name: '成功处理',
            Parameters: {
              value: 'high',
            },
          },
          {
            Id: 'failure',
            TypeName: 'Set',
            Name: '失败处理',
            Parameters: {
              value: 'low',
            },
          },
        ],
        Connections: [
          {
            SourceNodeId: 'trigger',
            SourcePortName: 'Output',
            TargetNodeId: 'condition',
            TargetPortName: 'Input',
          },
          {
            SourceNodeId: 'condition',
            SourcePortName: 'True',
            TargetNodeId: 'success',
            TargetPortName: 'Input',
          },
          {
            SourceNodeId: 'condition',
            SourcePortName: 'False',
            TargetNodeId: 'failure',
            TargetPortName: 'Input',
          },
        ],
      },
    },
  ];
}

function buildGuideJson(nodeTypes: NodeTypeDescriptorDto[], incomplete: boolean) {
  return {
    schema: workflowSchema,
    credentialNote,
    nodeTypes: groupByCategory(nodeTypes),
    examples: buildExamples(),
    commonErrors,
    ...(incomplete ? { incomplete: true } : {}),
  };
}

function buildGuideText(nodeTypes: NodeTypeDescriptorDto[], incomplete: boolean): string {
  const lines: string[] = [];
  lines.push('# Flow Engine DSL 编写指南');
  lines.push('');

  if (incomplete) {
    lines.push('> 注意：当前无法获取后端节点类型清单，以下内容为基础模板。');
    lines.push('');
  }

  lines.push('## 顶层结构');
  lines.push('```json');
  lines.push(JSON.stringify(workflowSchema.topLevel, undefined, 2));
  lines.push('```');
  lines.push('');

  lines.push('## 节点对象');
  lines.push('```json');
  lines.push(JSON.stringify(workflowSchema.node, undefined, 2));
  lines.push('```');
  lines.push('');

  lines.push('## 连接对象');
  lines.push('```json');
  lines.push(JSON.stringify(workflowSchema.connection, undefined, 2));
  lines.push('```');
  lines.push('');

  lines.push('## 凭据引用');
  lines.push(credentialNote);
  lines.push('');

  lines.push('## 支持的节点类型');
  const grouped = groupByCategory(nodeTypes);
  const categories = Object.keys(grouped);
  if (categories.length === 0) {
    lines.push('（无）');
  } else {
    for (const category of categories) {
      lines.push(`### ${category}`);
      for (const nodeType of grouped[category]) {
        lines.push(`- ${nodeType.typeName} (${nodeType.displayName})`);
      }
      lines.push('');
    }
  }
  lines.push('');

  lines.push('## 示例工作流');
  for (const example of buildExamples()) {
    const ex = example as { name: string; description: string; workflow: unknown };
    lines.push(`### ${ex.name}`);
    lines.push(ex.description);
    lines.push('```json');
    lines.push(JSON.stringify(ex.workflow, undefined, 2));
    lines.push('```');
    lines.push('');
  }

  lines.push('## 常见校验错误');
  for (const err of commonErrors) {
    lines.push(`- **${err.error}**：${err.message}`);
    lines.push(`  - 解决方法：${err.resolution}`);
  }

  return lines.join('\n');
}

export async function guide(options: GuideOptions): Promise<void> {
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

  if (isJsonMode()) {
    writeJson(buildGuideJson(nodeTypes, incomplete));
    return;
  }

  const text = buildGuideText(nodeTypes, incomplete);

  if (options.output) {
    writeFileSync(options.output, text, 'utf-8');
    log(`指南已写入：${options.output}`);
    return;
  }

  log(text);
}

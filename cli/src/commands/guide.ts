import { writeFileSync } from 'node:fs';
import { createClient, type ApiClientOptions } from '../api/client.js';
import { getConfig, type ConfigOptions } from '../config.js';
import { isJsonMode, isVerbose, log, writeJson, error } from '../output.js';
import type { NodeTypeDescriptorDto } from '../types.js';
import { BUILT_IN_NODE_TYPES } from './builtInNodeTypes.js';

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
    name: 'string (required)',
    projectId: 'string (optional)',
    nodes: 'NodeDefinition[] (required)',
    connections: 'ConnectionDefinition[] (required)',
    styleSettings: {
      layoutDirection: "'vertical' | 'horizontal'",
    },
  },
  node: {
    id: 'string (required)',
    typeName: 'string (required) — use "node-types list" to get available types',
    name: 'string (required)',
    parameters: 'Record<string, unknown> (required)',
    ports: 'PortInstance[] (required) — must match node type port definitions',
    positionX: 'number (required)',
    positionY: 'number (required)',
    isEntry: 'boolean (only one node should be true, typically the trigger)',
    errorStrategy: "'Terminate' | 'Continue' | 'Retry' (optional, default: Terminate)",
    retryPolicy: 'RetryPolicy (optional)',
    timeout: 'string — ISO 8601 duration (optional)',
  },
  port: {
    name: 'string (required) — must match a port defined by the node type',
    direction: "'Input' | 'Output' (required, string enum)",
    type: "'Main' | 'AgentTool' | 'LLM' | 'Memory' (required, string enum)",
  },
  connection: {
    id: 'string (required)',
    sourceNodeId: 'string (required)',
    sourcePortName: 'string (required) — must be an Output port on the source node',
    targetNodeId: 'string (required)',
    targetPortName: 'string (required) — must be an Input port on the target node',
    condition: 'string (optional) — expression for conditional connections',
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
    resolution: '至少设置一个 isEntry=true 的节点，并确保所有节点都在连接图中可达。',
  },
];

const recentProgress = [
  'DbUpsertNode（通用数据库 upsert，支持 PG/MySQL/MSSQL）',
  'PaginateNode（游标分页拉取，支持 cursor/offset）',
  'OAuth2 凭据类型 + 令牌自动托管（获取/缓存/刷新/重试）',
  'IfNode/FilterNode 条件表达式统一走表达式引擎（支持 $json/$credentials 等变量）',
];

const knownGaps = [
  '平台专用 SDK 节点（钉钉 / 企业微信 / 飞书）未提供，需用通用 OAuth2 + HTTP 节点自行组装。',
  '部分高级数据库功能（存储过程、复杂迁移）需自行扩展。',
  'authorization_code 等交互式授权、外部凭据保险库（Vault/KMS）对接尚未实现。',
];

const aiGeneration = {
  overview: 'Flow Engine 支持通过自然语言描述直接生成工作流 DSL。后端语义解析服务会结合实时节点类型清单构造 Prompt，调用 LLM 生成 JSON，并经过结构化校验与校验-纠错循环，保证草案可加载。',
  prerequisite: '需在后端 appsettings 的 "Ai" 节点配置可用的 LLM（如 OpenAI 兼容端点 + ApiKey），否则 generate 端点会返回友好错误。凭据/Token 不硬编码、不入日志。',
  commands: [
    'flowengine workflow generate --description "从钉钉拉取部门员工并写入数据库"',
    'flowengine workflow generate --description "..." --output dingtalk-sync.json   # 仅保存草案',
    'flowengine workflow generate --description "..." --create                       # 生成并询问后创建工作流',
    'flowengine workflow generate --description "..." --json                        # JSON 模式输出 { valid, draft, errors, attempts }',
  ],
  notes: [
    '返回的 draft 为工作流 JSON（含 name / nodes / connections），可直接作为 workflow create 的输入文件。',
    'valid=false 时附 errors（结构/类型/端口/连接/必填/凭据引用等错误），可在前端或再次描述时修正。',
    '钉钉令牌等 OAuth2 凭据由引擎按 provider 策略托管（GET gettoken?appkey&appsecret，自动缓存/刷新），下游通过 $credentials.<name>.accessToken 引用，不要自建专用节点。',
    '触发命令为 CLI 根级 execute [workflow-id]；execution 子命令组仅用于查询/取消执行记录。',
  ],
};

const variableReference = {
  overview:
    '节点参数与连接条件支持表达式。本引擎统一采用 `$` 前缀内建变量模型：所有 `$` 开头的变量都是引擎内建，裸写的名字视为用户数据或自定义变量（兼容 n8n 习惯）。以下为每个变量的含义与示例：',
  variables: [
    {
      syntax: '$json',
      meaning: '当前 item 的 Data（JsonNode），指向当前正在处理的数据项。',
      example: '$json.userid',
    },
    {
      syntax: '$input',
      meaning: 'n8n 式输入容器，提供 item()/all()/first()/last()/count()/Params/Context 方法。',
      example: '$input.item().userid',
    },
    {
      syntax: "$items(name?)",
      meaning: '获取指定（或当前）节点的全部 item 数据数组；name 为节点名。',
      example: "$items('GetUser') / $items()",
    },
    {
      syntax: "$node['NodeName']",
      meaning: '指定节点的输出对象（含 .json 数组）。',
      example: "$node['GetUser'].json[0].name",
    },
    {
      syntax: '$credentials.<name>.<field>',
      meaning: '多字段凭据值，引用已创建凭据的字段（如 accessToken、connectionString）。',
      example: '$credentials.db.connectionString',
    },
    {
      syntax: '$workflow',
      meaning: '工作流元数据（id / name / projectId / version）。',
      example: '$workflow.name',
    },
    {
      syntax: '$execution',
      meaning: '执行元数据（id 等）。',
      example: '$execution.id',
    },
    {
      syntax: '$env.VAR_NAME',
      meaning: '白名单环境变量（仅允许系统配置显式声明的变量，禁止敏感变量）。',
      example: '$env.API_BASE_URL',
    },
    {
      syntax: '$vars',
      meaning: '工作流级可写状态（当前为空对象占位）。',
      example: '$vars.flag',
    },
    {
      syntax: '$now',
      meaning: '当前 UTC 时间。',
      example: '$now',
    },
    {
      syntax: '$today',
      meaning: '当日 UTC 00:00。',
      example: '$today',
    },
    {
      syntax: '$runIndex',
      meaning: '当前运行索引（与 $itemIndex 一致）。',
      example: '$runIndex',
    },
    {
      syntax: '$itemIndex',
      meaning: '当前 item 索引（与 $runIndex 一致）。',
      example: '$itemIndex',
    },
    {
      syntax: '$cursor',
      meaning: 'PaginateNode 当前请求游标（仅 PaginateNode 注入）。',
      example: '$cursor',
    },
    {
      syntax: '$nextCursor',
      meaning: 'PaginateNode 下一页游标，用于 terminateWhen（仅 PaginateNode 注入）。',
      example: "$nextCursor == ''",
    },
  ],
  examples: [
    'httpRequest 节点 url 参数：https://oapi.dingtalk.com/user/list?access_token=$credentials.dingtalk.accessToken',
    "set 节点 fields：{ \"userId\": { \"source\": \"$json.userid\" } }",
    'if 节点 condition：$json.deptId == 1',
    "引用上游节点输出：$node['GetUser'].json[0].name",
  ],
};

const expressionSyntax = {
  overview:
    '表达式可写为「Script 对象」或「纯字符串简写」，二者等价。该写法与后端 SetNode 的 Script 改造（阶段一）保持一致。',
  scriptType:
    'Script 类型：{ source: string, returnType?: "String" | "Number" | "Boolean" | "Object" | "Array" }',
  plainStringShorthand:
    '纯字符串简写：一个裸字符串字面量（如 "$json.userid"）会被视作 { source: "...", returnType: "String" }，即默认按字符串表达式求值。',
  examples: [
    '{ source: "$json.userid", returnType: "String" }',
    '{ source: "$json.name + \' (\' + $json.dept + \')\'", returnType: "String" }',
    '纯字符串简写等价形式："$json.userid"',
  ],
};

function buildExamples(): unknown[] {
  return [
    {
      name: '基础 HTTP 请求工作流',
      description: '手动触发后执行一次 HTTP GET 请求。',
      workflow: {
        name: 'HelloHttp',
        nodes: [
          {
            id: 'start',
            typeName: 'manualTrigger',
            name: '开始',
            parameters: {},
            ports: [
              { name: 'Output', direction: 'Output', type: 'Main' },
            ],
            positionX: 100,
            positionY: 100,
            isEntry: true,
          },
          {
            id: 'http',
            typeName: 'httpRequest',
            name: '请求示例接口',
            parameters: {
              method: 'GET',
              url: 'https://api.example.com/items',
            },
            ports: [
              { name: 'Input', direction: 'Input', type: 'Main' },
              { name: 'Output', direction: 'Output', type: 'Main' },
            ],
            positionX: 300,
            positionY: 100,
          },
        ],
        connections: [
          {
            id: 'conn-1',
            sourceNodeId: 'start',
            sourcePortName: 'Output',
            targetNodeId: 'http',
            targetPortName: 'Input',
          },
        ],
      },
    },
    {
      name: '条件分支工作流',
      description: '根据输入数据选择不同分支。',
      workflow: {
        name: 'ConditionalFlow',
        nodes: [
          {
            id: 'trigger',
            typeName: 'manualTrigger',
            name: '触发器',
            parameters: {},
            ports: [
              { name: 'Output', direction: 'Output', type: 'Main' },
            ],
            positionX: 100,
            positionY: 100,
            isEntry: true,
          },
          {
            id: 'condition',
            typeName: 'if',
            name: '判断',
            parameters: {
              condition: '={{ $json.value > 10 }}',
            },
            ports: [
              { name: 'Input', direction: 'Input', type: 'Main' },
              { name: 'True', direction: 'Output', type: 'Main' },
              { name: 'False', direction: 'Output', type: 'Main' },
            ],
            positionX: 300,
            positionY: 100,
          },
          {
            id: 'success',
            typeName: 'set',
            name: '成功处理',
            parameters: {
              fields: { result: 'high' },
              include: 'All',
            },
            ports: [
              { name: 'Input', direction: 'Input', type: 'Main' },
              { name: 'Output', direction: 'Output', type: 'Main' },
            ],
            positionX: 500,
            positionY: 0,
          },
          {
            id: 'failure',
            typeName: 'set',
            name: '失败处理',
            parameters: {
              fields: { result: 'low' },
              include: 'All',
            },
            ports: [
              { name: 'Input', direction: 'Input', type: 'Main' },
              { name: 'Output', direction: 'Output', type: 'Main' },
            ],
            positionX: 500,
            positionY: 200,
          },
        ],
        connections: [
          {
            id: 'conn-1',
            sourceNodeId: 'trigger',
            sourcePortName: 'Output',
            targetNodeId: 'condition',
            targetPortName: 'Input',
          },
          {
            id: 'conn-2',
            sourceNodeId: 'condition',
            sourcePortName: 'True',
            targetNodeId: 'success',
            targetPortName: 'Input',
          },
          {
            id: 'conn-3',
            sourceNodeId: 'condition',
            sourcePortName: 'False',
            targetNodeId: 'failure',
            targetPortName: 'Input',
          },
        ],
      },
    },
    {
      name: '钉钉员工同步到数据库',
      description: '凭据使用钉钉 OAuth2（provider=dingtalk，引擎自动 GET gettoken 并托管 accessToken）与数据库连接凭据，分页拉取部门员工并 upsert 到数据库。',
      workflow: {
        name: 'DingtalkEmployeeSync',
        nodes: [
          {
            id: 'trigger',
            typeName: 'manualTrigger',
            name: '触发器',
            parameters: {},
            ports: [
              { name: 'Output', direction: 'Output', type: 'Main' },
            ],
            positionX: 100,
            positionY: 100,
            isEntry: true,
          },
          {
            id: 'fetch',
            typeName: 'paginate',
            name: '拉取钉钉员工',
            parameters: {
              url: 'https://oapi.dingtalk.com/topapi/v2/user/list?access_token=$credentials.dingtalk.accessToken',
              method: 'POST',
              body: { dept_id: 1, cursor: 0, size: 100 },
              itemsPath: 'result.list',
              nextCursorPath: 'result.next_cursor',
              terminateWhen: '$nextCursor == ""',
              cursorType: 'string',
            },
            ports: [
              { name: 'Input', direction: 'Input', type: 'Main' },
              { name: 'Output', direction: 'Output', type: 'Main' },
            ],
            positionX: 300,
            positionY: 100,
          },
          {
            id: 'upsert',
            typeName: 'dbUpsert',
            name: '写入数据库',
            parameters: {
              connection: '$credentials.db.connectionString',
              mode: 'upsert',
              keyColumns: ['emp_id'],
            },
            ports: [
              { name: 'Input', direction: 'Input', type: 'Main' },
              { name: 'Output', direction: 'Output', type: 'Main' },
            ],
            positionX: 500,
            positionY: 100,
          },
        ],
        connections: [
          {
            id: 'conn-1',
            sourceNodeId: 'trigger',
            sourcePortName: 'Output',
            targetNodeId: 'fetch',
            targetPortName: 'Input',
          },
          {
            id: 'conn-2',
            sourceNodeId: 'fetch',
            sourcePortName: 'Output',
            targetNodeId: 'upsert',
            targetPortName: 'Input',
          },
        ],
      },
    },
    {
      name: 'SetNode 表达式字段映射',
      description: 'SetNode 通过表达式将上游字段重命名/拼接后输出，等价于轻量级字段映射。',
      workflow: {
        name: 'SetNodeMapping',
        nodes: [
          {
            id: 'trigger',
            typeName: 'manualTrigger',
            name: '触发器',
            parameters: {},
            ports: [
              { name: 'Output', direction: 'Output', type: 'Main' },
            ],
            positionX: 100,
            positionY: 100,
            isEntry: true,
          },
          {
            id: 'map',
            typeName: 'set',
            name: '字段映射',
            parameters: {
              fields: {
                userId: { source: '$json.userid', returnType: 'String' },
                fullName: { source: "$json.name + ' (' + $json.dept + ')'", returnType: 'String' },
                active: true,
              },
              include: 'All',
            },
            ports: [
              { name: 'Input', direction: 'Input', type: 'Main' },
              { name: 'Output', direction: 'Output', type: 'Main' },
            ],
            positionX: 300,
            positionY: 100,
          },
        ],
        connections: [
          {
            id: 'conn-1',
            sourceNodeId: 'trigger',
            sourcePortName: 'Output',
            targetNodeId: 'map',
            targetPortName: 'Input',
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
    aiGeneration,
    variableReference,
    expressionSyntax,
    ...(incomplete
      ? { incomplete: true, offlineNotice: '未连接后端，节点类型清单不可用。以下为基础模板与已知内置能力。', recentProgress, knownGaps }
      : { recentProgress }),
  };
}

function buildGuideText(nodeTypes: NodeTypeDescriptorDto[], incomplete: boolean): string {
  const lines: string[] = [];
  lines.push('# Flow Engine DSL 编写指南');
  lines.push('');

  if (incomplete) {
    lines.push('> 未连接后端，节点类型清单不可用。以下为基础模板与已知内置能力。');
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

  if (incomplete) {
    lines.push('## 近期已补齐能力');
    for (const p of recentProgress) {
      lines.push(`- ${p}`);
    }
    lines.push('');
    lines.push('## 已知能力缺口');
    for (const gap of knownGaps) {
      lines.push(`- ${gap}`);
    }
    lines.push('');
  }

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

  lines.push('## AI 生成工作流（自然语言 → DSL）');
  lines.push(aiGeneration.overview);
  lines.push('');
  lines.push(`前提：${aiGeneration.prerequisite}`);
  lines.push('');
  lines.push('常用命令：');
  for (const cmd of aiGeneration.commands) {
    lines.push(`- \`${cmd}\``);
  }
  lines.push('');
  lines.push('说明：');
  for (const note of aiGeneration.notes) {
    lines.push(`- ${note}`);
  }
  lines.push('');

  lines.push('## 表达式变量参考');
  lines.push(variableReference.overview);
  lines.push('');
  for (const v of variableReference.variables) {
    lines.push(`- \`${v.syntax}\`：${v.meaning}（示例：\`${v.example}\`）`);
  }
  lines.push('');
  lines.push('示例：');
  for (const ex of variableReference.examples) {
    lines.push(`- ${ex}`);
  }
  lines.push('');

  lines.push('## 表达式语法说明');
  lines.push(expressionSyntax.overview);
  lines.push('');
  lines.push(`Script 类型：${expressionSyntax.scriptType}`);
  lines.push('');
  lines.push(expressionSyntax.plainStringShorthand);
  lines.push('');
  lines.push('示例：');
  for (const ex of expressionSyntax.examples) {
    lines.push(`- \`${ex}\``);
  }
  lines.push('');

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
    nodeTypes = BUILT_IN_NODE_TYPES.map((n) => ({
      typeName: n.typeName,
      displayName: n.displayName,
      category: n.category,
      executionMode: 'OnceForAll' as const,
      defaultIsEntry: false,
      parameters: [],
      ports: [],
    }));
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

export interface BuiltInPortDescriptor {
  name: string;
  direction: 'Input' | 'Output';
  type: 'Main' | string;
}

export interface BuiltInParameterDescriptor {
  name: string;
  required: boolean;
}

export interface BuiltInNodeTypeDescriptor {
  typeName: string;
  displayName: string;
  category: string;
  ports: BuiltInPortDescriptor[];
  parameters: BuiltInParameterDescriptor[];
}

export const BUILT_IN_NODE_TYPES: BuiltInNodeTypeDescriptor[] = [
  // Triggers
  {
    typeName: 'manualTrigger',
    displayName: '手动触发器',
    category: 'Trigger',
    ports: [{ name: 'Output', direction: 'Output', type: 'Main' }],
    parameters: [],
  },
  {
    typeName: 'scheduleTrigger',
    displayName: '定时触发器',
    category: 'Trigger',
    ports: [{ name: 'Output', direction: 'Output', type: 'Main' }],
    parameters: [{ name: 'schedule', required: false }],
  },
  {
    typeName: 'webhook',
    displayName: 'Webhook 触发器',
    category: 'Trigger',
    ports: [{ name: 'Output', direction: 'Output', type: 'Main' }],
    parameters: [{ name: 'path', required: false }],
  },

  // Integrations
  {
    typeName: 'httpRequest',
    displayName: 'HTTP 请求',
    category: 'Integration',
    ports: [
      { name: 'Input', direction: 'Input', type: 'Main' },
      { name: 'Output', direction: 'Output', type: 'Main' },
    ],
    parameters: [
      { name: 'method', required: true },
      { name: 'url', required: true },
    ],
  },
  {
    typeName: 'oauth2',
    displayName: 'OAuth2 令牌',
    category: 'Integration',
    ports: [
      { name: 'Input', direction: 'Input', type: 'Main' },
      { name: 'Output', direction: 'Output', type: 'Main' },
    ],
    parameters: [{ name: 'credentialName', required: true }],
  },
  {
    typeName: 'paginate',
    displayName: '分页拉取',
    category: 'Integration',
    ports: [
      { name: 'Input', direction: 'Input', type: 'Main' },
      { name: 'Output', direction: 'Output', type: 'Main' },
    ],
    parameters: [
      { name: 'url', required: true },
      { name: 'nextCursorPath', required: true },
      { name: 'itemsPath', required: true },
      { name: 'terminateWhen', required: true },
    ],
  },
  {
    typeName: 'dbUpsert',
    displayName: '数据库 Upsert',
    category: 'Integration',
    ports: [
      { name: 'Input', direction: 'Input', type: 'Main' },
      { name: 'Output', direction: 'Output', type: 'Main' },
    ],
    parameters: [
      { name: 'connection', required: true },
      { name: 'table', required: true },
      { name: 'keyColumns', required: true },
      { name: 'columns', required: true },
      { name: 'mode', required: false },
    ],
  },

  // Logic
  {
    typeName: 'if',
    displayName: '条件判断',
    category: 'Logic',
    ports: [
      { name: 'Input', direction: 'Input', type: 'Main' },
      { name: 'True', direction: 'Output', type: 'Main' },
      { name: 'False', direction: 'Output', type: 'Main' },
    ],
    parameters: [{ name: 'condition', required: true }],
  },
  {
    typeName: 'switch',
    displayName: '多路分支',
    category: 'Logic',
    ports: [
      { name: 'Input', direction: 'Input', type: 'Main' },
      { name: 'Output', direction: 'Output', type: 'Main' },
    ],
    parameters: [{ name: 'expression', required: false }],
  },
  {
    typeName: 'filter',
    displayName: '数据过滤',
    category: 'Logic',
    ports: [
      { name: 'Input', direction: 'Input', type: 'Main' },
      { name: 'Output', direction: 'Output', type: 'Main' },
    ],
    parameters: [{ name: 'condition', required: true }],
  },
  {
    typeName: 'loop',
    displayName: '循环',
    category: 'Logic',
    ports: [
      { name: 'Input', direction: 'Input', type: 'Main' },
      { name: 'Output', direction: 'Output', type: 'Main' },
    ],
    parameters: [{ name: 'batchSize', required: false }],
  },
  {
    typeName: 'set',
    displayName: '设置字段',
    category: 'Logic',
    ports: [
      { name: 'Input', direction: 'Input', type: 'Main' },
      { name: 'Output', direction: 'Output', type: 'Main' },
    ],
    parameters: [{ name: 'fields', required: true }],
  },
  {
    typeName: 'script',
    displayName: 'JS 脚本',
    category: 'Logic',
    ports: [
      { name: 'Input', direction: 'Input', type: 'Main' },
      { name: 'Output', direction: 'Output', type: 'Main' },
    ],
    parameters: [{ name: 'code', required: true }],
  },

  // Utility
  {
    typeName: 'merge',
    displayName: '合并',
    category: 'Utility',
    ports: [
      { name: 'Input', direction: 'Input', type: 'Main' },
      { name: 'Output', direction: 'Output', type: 'Main' },
    ],
    parameters: [],
  },
  {
    typeName: 'aggregate',
    displayName: '聚合',
    category: 'Utility',
    ports: [
      { name: 'Input', direction: 'Input', type: 'Main' },
      { name: 'Output', direction: 'Output', type: 'Main' },
    ],
    parameters: [{ name: 'operation', required: false }],
  },
  {
    typeName: 'wait',
    displayName: '等待',
    category: 'Utility',
    ports: [
      { name: 'Input', direction: 'Input', type: 'Main' },
      { name: 'Output', direction: 'Output', type: 'Main' },
    ],
    parameters: [{ name: 'duration', required: true }],
  },
];

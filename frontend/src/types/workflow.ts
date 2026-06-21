export interface Option {
  label: string;
  value: string;
}

export interface WorkflowStyleSettings {
  layoutDirection: 'vertical' | 'horizontal';
}

export const DEFAULT_STYLE_SETTINGS: WorkflowStyleSettings = {
  layoutDirection: 'vertical',
};

export interface DisplayRule {
  condition: string;
  dependencies: string[];
}

/**
 * 参数渲染提示，与后端 PresentationHint 枚举对应。
 * 未指定时由前端 FieldResolver 按 type 自动推断。
 */
export type PresentationHint =
  | 'Default'
  | 'ButtonGroup'
  | 'Select'
  | 'TextArea'
  | 'CodeEditor'
  | 'JsonEditor'
  | 'KeyValueEditor'
  | 'Toggle'
  | 'Secret'
  | 'CredentialSelect'
  | 'ResourceSelect'
  | 'FileUpload'
  | 'Expression'
  | 'Array'
  | 'DateTime';

/**
 * 参数类型，与后端 ParameterType 枚举对应。
 */
export type ParameterType =
  | 'String'
  | 'Number'
  | 'Boolean'
  | 'Options'
  | 'Json'
  | 'Code'
  | 'Credential'
  | 'Resource'
  | 'Array'
  | 'File'
  | 'Expression';

export interface ParameterDefinition {
  name: string;
  displayName: string;
  type: ParameterType;
  defaultValue: unknown;
  required: boolean;
  validationRules: string[];
  displayRule: DisplayRule | null;
  credentialType: string | null;
  options: Option[];
  /** 渲染提示，指导前端使用何种组件渲染。 */
  hint?: PresentationHint | null;
  /** 字段描述，展示在参数下方。 */
  description?: string | null;
  /** 资源类型，用于 Resource 类型指定资源来源。 */
  resourceType?: string | null;
  /** 子项定义，用于 Array 类型定义列表每一行的结构。 */
  itemDefinition?: ParameterDefinition | null;
  /** 子字段列表，用于结构化数组子项（如 SwitchCase 的 Name/Label/Value）。 */
  fields?: ParameterDefinition[];
}

export interface PortDefinition {
  name: string;
  displayName: string;
  direction: 'Input' | 'Output';
  type: string;
  required: boolean;
  condition?: string | null;
}

export interface NodeTypeDescriptor {
  typeName: string;
  displayName: string;
  category: string;
  icon: string;
  executionMode: string;
  parameters: ParameterDefinition[];
  ports: PortDefinition[];
  defaultIsEntry: boolean;
  /** 节点画布上显示的模板，用 {{paramName}} 引用参数值 */
  displayTemplate?: string | null;
}

export interface NodeDefinition {
  id: string;
  typeName: string;
  name: string;
  parameters: Record<string, unknown>;
  ports: PortDefinition[];
  positionX: number;
  positionY: number;
  isEntry: boolean;
  disabled: boolean;
  errorStrategy: string;
  retryPolicy: string | null;
  timeout: number | null;
}

export interface Connection {
  id: string;
  sourceNodeId: string;
  sourcePortName: string;
  targetNodeId: string;
  targetPortName: string;
}

export interface Workflow {
  id: string;
  projectId: string | null;
  name: string;
  version: number;
  createdBy: string;
  createdAt: string;
  updatedAt: string;
  isActive: boolean;
  styleSettings: WorkflowStyleSettings | null;
  nodes: NodeDefinition[];
  connections: Connection[];
}

export interface WorkflowSummary {
  id: string;
  name: string;
  version: number;
  createdBy: string;
  createdAt: string;
  updatedAt: string;
  isActive: boolean;
}

export interface CreateWorkflowDto {
  name: string;
  createdBy: string;
  nodes: NodeDefinition[];
  connections: Connection[];
}

export interface UpdateWorkflowDto {
  name: string;
  isActive: boolean;
  styleSettings: WorkflowStyleSettings | null;
  nodes: NodeDefinition[];
  connections: Connection[];
}

export type ExecutionStatus = 'Pending' | 'Running' | 'Completed' | 'Failed' | 'Cancelled';

export interface NodeExecutionRecordDto {
  id: string;
  nodeDefinitionId: string;
  runIndex: number;
  status: ExecutionStatus;
  startedAt: string | null;
  completedAt: string | null;
  inputs: Record<string, unknown> | null;
  output: unknown;
  rawParameters: Record<string, unknown> | null;
  resolvedParameters: Record<string, unknown> | null;
}

export interface ExecutionDto {
  id: string;
  workflowDefinitionId: string;
  status: ExecutionStatus;
  startedAt: string | null;
  completedAt: string | null;
  nodeRecords: NodeExecutionRecordDto[];
}

export interface CredentialDto {
  id: string;
  name: string;
  type: string;
  createdAt: string;
  updatedAt: string;
}

export interface CreateCredentialDto {
  name: string;
  type: string;
  data: Record<string, unknown>;
}

export interface UpdateCredentialDto {
  name: string;
  type: string;
  data: Record<string, unknown>;
}

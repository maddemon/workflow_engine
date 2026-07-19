export interface Option {
  label: string;
  value: string;
}

export interface WorkflowStyleSettings {
  layoutDirection: 'vertical' | 'horizontal';
}

export const DEFAULT_STYLE_SETTINGS: WorkflowStyleSettings = {
  layoutDirection: 'horizontal',
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
  | 'Script'
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
  /** Hint 组件的扩展属性（如 Script 的 language 配置）。 */
  hintProperties?: Record<string, unknown> | null;
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
  positionX: number | null;
  positionY: number | null;
  isEntry: boolean;
  disabled: boolean;
  errorStrategy: string;
  retryPolicy: string | null;
  timeout: number | null;
}

export interface Connection {
  id: string;
  sourceNodeId: string;
  sourcePortName: string | null;
  targetNodeId: string;
  targetPortName: string | null;
  condition?: string;
}

export interface StructuredDiff {
  op: string;
  nodeId?: string;
  field?: string;
  before?: unknown;
  after?: unknown;
}

export interface ValidationError {
  nodeId?: string;
  field?: string;
  errorType: string;
  message: string;
  schema?: unknown;
  suggestedFix?: string;
}

export interface ValidateWorkflowResult {
  valid: boolean;
  errors: ValidationError[];
  canAutoFix: boolean;
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
  source?: 'ai' | 'human';
  draftStatus?: 'pending' | 'rejected' | 'confirmed';
  rejectionReason?: string | null;
  diff?: StructuredDiff[];
  styleSettings: WorkflowStyleSettings | null;
  nodes: NodeDefinition[];
  connections: Connection[];
}

export interface WorkflowSummary {
  id: string;
  name: string;
  version: number;
  isActive: boolean;
  /** 项目 ID（null 表示全局工作流）。 */
  projectId: string | null;
  createdAt: string;
  updatedAt: string | null;
  source?: 'ai' | 'human';
  draftStatus?: 'pending' | 'rejected' | 'confirmed';
  rejectionReason?: string | null;
  diff?: StructuredDiff[];
  /** 最近一次执行完成时间。 */
  lastExecutionAt: string | null;
  /** 关联触发器数量。 */
  triggerCount: number;
  /** 下次触发时间（最近的一个）。 */
  nextTriggerAt: string | null;
}

export interface CreateWorkflowDto {
  name: string;
  createdBy: string;
  /** 项目 ID，省略或 null 表示全局工作流。 */
  projectId?: string | null;
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

export type ExecutionStatus = 'Pending' | 'Running' | 'Completed' | 'Failed' | 'Cancelled' | 'DryRunCompleted';

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
  /** 失败时的错误信息（仅 execution_failed 时填充）。 */
  error?: { code: string; message: string } | null;
  nodeRecords: NodeExecutionRecordDto[];
}

export interface ExecutionSummaryDto {
  id: string;
  workflowDefinitionId: string;
  status: ExecutionStatus;
  startedAt: string | null;
  completedAt: string | null;
}

export interface CredentialDto {
  id: string;
  projectId: string | null;
  name: string;
  type: string;
  /** 凭据明文字段（已解密），查看者角色下将被脱敏为 *** */
  fields: Record<string, string>;
  createdAt: string;
  updatedAt: string;
}

export interface CreateCredentialDto {
  name: string;
  type: string;
  fields: Record<string, string>;
}

export interface UpdateCredentialDto {
  name: string;
  fields: Record<string, string>;
}

export interface CredentialFieldDefinition {
  name: string;
  displayName: string;
  required: boolean;
  sensitive: boolean;
  hint?: string;
}

export interface CredentialTypeDefinition {
  name: string;
  displayName: string;
  fields: CredentialFieldDefinition[];
}

export interface DryRunRequest {
  nodes: NodeDefinition[];
  connections: Connection[];
  inputs?: Record<string, unknown>;
  credentials?: Array<{ name: string; type: string; fields: Record<string, string> }>;
}

// --- Triggers ---

export interface TriggerSettingsDto {
  // Schedule 类型
  cronExpression?: string | null;
  timeZone?: string | null;
  startAt?: string | null;
  endAt?: string | null;
  // Webhook 类型
  webhookPath?: string | null;
  secret?: string | null;
  allowedIps?: string[] | null;
  allowedOrigins?: string[] | null;
  isSync?: boolean;
  maxWaitSeconds?: number;
  // Poll 类型
  intervalSeconds?: number;
  timeoutSeconds?: number;
  pollNodeId?: string | null;
  dedupStrategy?: string;
  skipIfRunning?: boolean;
  lastPollId?: string | null;
  lastPollTime?: string | null;
}

export type TriggerType = 'Schedule' | 'Webhook' | 'Poll';

export interface TriggerDto {
  id: string;
  workflowDefinitionId: string;
  workflowVersion: number;
  type: TriggerType;
  name: string;
  isActive: boolean;
  settings?: TriggerSettingsDto | null;
  lastTriggeredAt?: string | null;
  nextTriggerAt?: string | null;
  updatedAt?: string | null;
}

export interface CreateTriggerDto {
  workflowDefinitionId: string;
  workflowVersion: number;
  type: TriggerType;
  name: string;
  isActive?: boolean;
  settings?: TriggerSettingsDto | null;
}

export interface UpdateTriggerDto {
  name: string;
  isActive: boolean;
  settings?: TriggerSettingsDto | null;
}

// --- Auth ---

export interface LoginRequest {
  email: string;
  password: string;
}

export interface LoginResult {
  success: boolean;
  token?: string;
  user?: UserDto;
  errorMessage?: string;
}

export interface UserDto {
  id: string;
  email: string;
  userName: string;
  displayName: string;
  roles: string[];
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
}

// --- Projects ---

export interface ProjectDto {
  id: string;
  name: string;
  description: string | null;
  createdBy: string;
  createdAt: string;
  updatedAt: string | null;
}

export interface CreateProjectDto {
  name: string;
  description?: string | null;
}

export interface UpdateProjectDto {
  name: string;
  description?: string | null;
}



// --- Workflow Import/Export ---

export interface WorkflowExportResult {
  name: string;
  version: number;
  nodes: unknown[];
  connections: unknown[];
  exportedAt: string;
  exportedBy: string;
  styleSettings?: Record<string, unknown> | null;
}

export interface ImportError {
  errorType: string;
  message: string;
  nodeId?: string | null;
  connectionId?: string | null;
}

export interface ImportResult {
  success: boolean;
  workflowId?: string | null;
  workflowName?: string | null;
  errors: ImportError[];
}

export interface BatchImportResult {
  successCount: number;
  failureCount: number;
  results: ImportResult[];
}

export interface ImportWorkflowRequest {
  json: string;
  projectId?: string | null;
  importedBy: string;
}

export interface ImportBatchRequest {
  json: string;
  projectId?: string | null;
  importedBy: string;
}

export interface ExportBatchRequest {
  ids: string[];
}

// --- API Keys (Personal Access Tokens) ---

export interface CreateApiKeyResult {
  id: string;
  name: string;
  prefix: string;
  expiresAt: string | null;
  /** 明文 Key（仅创建时返回一次，前缀为 fe_）。 */
  key: string;
}

// 与后端 FlowEngine.Application.Dtos / FlowEngine.Core 对应的手写 DTO 类型
// 日期时间字段在后端序列化后为 ISO 字符串，统一使用 string。

export type ErrorStrategy = 'Terminate' | 'Continue' | 'Retry';

export type BackoffStrategy = 'Exponential' | 'Linear' | 'Fixed';

export type PortDirection = 'Input' | 'Output';

export type PortType = 'Main' | 'AgentTool' | 'LLM' | 'Memory';

export type TriggerType = 'Schedule' | 'Webhook' | 'Poll';

export interface WorkflowStyleSettings {
  layoutDirection: 'vertical' | 'horizontal';
}

export interface PortInstance {
  name: string;
  direction: PortDirection;
  type: PortType;
}

export interface RetryPolicy {
  maxRetries: number;
  baseDelay: string;
  maxDelay: string;
  useJitter: boolean;
  backoffStrategy: BackoffStrategy;
  retryableErrorCodes?: string[];
}

export interface NodeDefinitionDto {
  id: string;
  typeName: string;
  name: string;
  parameters: Record<string, unknown>;
  ports: PortInstance[];
  positionX: number;
  positionY: number;
  isEntry: boolean;
  retryPolicy?: RetryPolicy;
  errorStrategy: ErrorStrategy;
  timeout?: string;
}

export interface ConnectionDto {
  id: string;
  sourceNodeId: string;
  sourcePortName: string;
  targetNodeId: string;
  targetPortName: string;
  condition?: string;
}

export interface CreateWorkflowDto {
  projectId?: string;
  name: string;
  createdBy: string;
  styleSettings?: WorkflowStyleSettings;
  nodes: NodeDefinitionDto[];
  connections: ConnectionDto[];
}

export interface UpdateWorkflowDto {
  name: string;
  isActive: boolean;
  styleSettings?: WorkflowStyleSettings;
  nodes: NodeDefinitionDto[];
  connections: ConnectionDto[];
}

export interface WorkflowDto {
  id: string;
  projectId?: string;
  name: string;
  version: number;
  createdBy: string;
  createdAt: string;
  updatedAt?: string;
  isActive: boolean;
  styleSettings?: WorkflowStyleSettings;
  nodes: NodeDefinitionDto[];
  connections: ConnectionDto[];
}

export interface WorkflowSummaryDto {
  id: string;
  name: string;
  version: number;
  isActive: boolean;
  projectId?: string;
  createdAt: string;
  updatedAt?: string;
  lastExecutionAt?: string;
  triggerCount: number;
  nextTriggerAt?: string;
}

export interface PagedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

export interface DryRunCredentialDto {
  name: string;
  type: string;
  fields: Record<string, string>;
}

export interface DryRunWorkflowRequestDto {
  nodes: NodeDefinitionDto[];
  connections: ConnectionDto[];
  inputs?: Record<string, unknown>;
  credentials?: DryRunCredentialDto[];
}

export interface NodeExecutionRecordDto {
  id: string;
  nodeDefinitionId: string;
  runIndex: number;
  status: string;
  startedAt: string;
  completedAt?: string;
  inputs?: Record<string, unknown>;
  output?: unknown;
  rawParameters?: Record<string, unknown>;
  resolvedParameters?: Record<string, unknown>;
}

export interface ExecutionDto {
  id: string;
  workflowDefinitionId: string;
  status: string;
  startedAt: string;
  completedAt?: string;
  nodeRecords: NodeExecutionRecordDto[];
}

export interface ExecutionSummaryDto {
  id: string;
  workflowDefinitionId: string;
  status: string;
  startedAt: string;
  completedAt?: string;
}

export interface StartExecutionDto {
  workflowId: string;
}

export interface RegisterRequest {
  email: string;
  userName: string;
  password: string;
  displayName?: string;
}

export interface RegisterResult {
  success: boolean;
  userId?: string;
  errorMessage?: string;
}

export interface LoginRequest {
  email: string;
  password: string;
}

export interface LoginResult {
  success: boolean;
  token?: string;
  userId?: string;
  user?: UserDto;
  errorMessage?: string;
}

export interface UserDto {
  id: string;
  email: string;
  userName: string;
  displayName?: string;
  isActive: boolean;
  createdAt: string;
  updatedAt?: string;
}

export interface AssignRoleRequest {
  role: string;
}

export interface CreateApiKeyRequest {
  name: string;
  expiresAt?: string;
}

export interface CreateApiKeyResult {
  id: string;
  name: string;
  prefix: string;
  expiresAt?: string;
  key: string;
}

export interface ApiKeyDto {
  id: string;
  name: string;
  prefix: string;
  createdAt: string;
  expiresAt?: string;
  revokedAt?: string;
}

export interface ProjectDto {
  id: string;
  name: string;
  description?: string;
  createdBy: string;
  createdAt: string;
  updatedAt?: string;
}

export interface CreateProjectDto {
  name: string;
  description?: string;
}

export interface UpdateProjectDto {
  name: string;
  description?: string;
}

export interface ProjectMemberDto {
  id: string;
  projectId: string;
  userId: string;
  role: string;
  createdAt: string;
}

export interface AddProjectMemberDto {
  userId: string;
  role: string;
}

export interface UpdateProjectMemberDto {
  role: string;
}

export interface CreateCredentialDto {
  projectId?: string;
  name: string;
  type: string;
  fields: Record<string, string>;
}

export interface UpdateCredentialDto {
  name: string;
  fields: Record<string, string>;
}

export interface CredentialDto {
  id: string;
  projectId?: string;
  name: string;
  type: string;
  fields: Record<string, string>;
  createdAt: string;
  updatedAt?: string;
}

export interface TriggerSettingsDto {
  cronExpression?: string;
  timeZone?: string;
  startAt?: string;
  endAt?: string;
  webhookPath?: string;
  secret?: string;
  allowedIps?: string[];
  allowedOrigins?: string[];
  isSync?: boolean;
  maxWaitSeconds?: number;
  intervalSeconds?: number;
  timeoutSeconds?: number;
  pollNodeId?: string;
  dedupStrategy?: string;
  skipIfRunning?: boolean;
  lastPollId?: string;
  lastPollTime?: string;
}

export interface TriggerDto {
  id: string;
  workflowDefinitionId: string;
  workflowVersion: number;
  type: TriggerType;
  name: string;
  isActive: boolean;
  settings?: TriggerSettingsDto;
  lastTriggeredAt?: string;
  nextTriggerAt?: string;
  updatedAt?: string;
}

export interface CreateTriggerDto {
  workflowDefinitionId: string;
  workflowVersion: number;
  type: TriggerType;
  name: string;
  isActive?: boolean;
  settings?: TriggerSettingsDto;
}

export interface UpdateTriggerDto {
  name: string;
  isActive: boolean;
  settings?: TriggerSettingsDto;
}

export interface WebhookRouteDto {
  id: string;
  path: string;
  method: string;
  workflowDefinitionId: string;
  triggerId: string;
}

export interface WorkflowExportResult {
  name: string;
  version: number;
  nodes: unknown[];
  connections: unknown[];
  exportedAt: string;
  exportedBy: string;
  styleSettings?: Record<string, unknown>;
}

export interface ImportError {
  errorType: string;
  message: string;
  nodeId?: string;
  connectionId?: string;
}

export interface ImportResult {
  success: boolean;
  workflowId?: string;
  workflowName?: string;
  errors: ImportError[];
}

export interface BatchImportResult {
  successCount: number;
  failureCount: number;
  results: ImportResult[];
}

export interface ImportWorkflowRequest {
  json: string;
  projectId?: string;
  importedBy: string;
}

export interface ImportBatchRequest {
  json: string;
  projectId?: string;
  importedBy: string;
}

export interface ExportBatchRequest {
  ids: string[];
}

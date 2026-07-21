import axios from 'axios';
import i18n from '../i18n.ts';
import type {
  NodeTypeDescriptor,
  Workflow,
  WorkflowSummary,
  CreateWorkflowDto,
  UpdateWorkflowDto,
  ExecutionDto,
  ExecutionSummaryDto,
  CredentialDto,
  CredentialTypeDefinition,
  CreateCredentialDto,
  DryRunRequest,
  UpdateCredentialDto,
  TriggerDto,
  CreateTriggerDto,
  UpdateTriggerDto,
  LoginRequest,
  LoginResult,
  UserDto,
  ProjectDto,
  CreateProjectDto,
  UpdateProjectDto,
  WorkflowExportResult,
  ImportResult,
  BatchImportResult,
  ImportWorkflowRequest,
  ImportBatchRequest,
  ExportBatchRequest,
  ValidateWorkflowResult,
  CreateApiKeyResult,
  PagedResult,
} from '../types/workflow.ts';

const api = axios.create({
  baseURL: '/api/v1',
  headers: { 'Content-Type': 'application/json' },
  withCredentials: true,
});

api.interceptors.request.use((config) => {
  config.headers.set('Accept-Language', i18n.resolvedLanguage ?? 'en');
  return config;
});

/** 结构化 API 错误，统一前端错误处理（R10）。 */
export class ApiError extends Error {
  status: number;
  code?: string;
  details?: unknown;

  constructor(status: number, message: string, code?: string, details?: unknown) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.code = code;
    this.details = details;
  }
}

api.interceptors.response.use(
  (response) => response,
  (error) => {
    if (error.response) {
      const { status, data } = error.response;
      const message =
        (data && (data.message ?? data.title)) ??
        error.message ??
        'Request failed';
      const code = data?.errorCode ?? data?.code ?? data?.type;

      if (status === 401) {
        localStorage.removeItem('auth_user');
        // 不要在每次 401 都强制整页跳转 /login：/auth/me 未登录本就会返回 401，
        // 整页刷新会重新挂载 AuthProvider 再次请求，形成 401→刷新→401 死循环
        // （速率限制触发后又表现为 429 死循环）。是否跳转交给 React Router 的
        // ProtectedRoute / AuthLayout 依据 isAuthenticated 处理即可。
        if (window.location.pathname !== '/login') {
          window.location.href = '/login';
        }
      }

      return Promise.reject(new ApiError(status, message, code, data));
    }

    return Promise.reject(new ApiError(0, error?.message ?? 'Network error'));
  },
);

export async function getNodeTypes(category?: string): Promise<NodeTypeDescriptor[]> {
  const params = category ? { category } : {};
  const res = await api.get<NodeTypeDescriptor[]>('/node-types', { params });
  return res.data;
}

export async function getWorkflows(): Promise<WorkflowSummary[]> {
  const res = await api.get<{ items: WorkflowSummary[]; totalCount: number }>('/workflows');
  return res.data.items;
}

export async function getWorkflow(id: string): Promise<Workflow> {
  const res = await api.get<Workflow>(`/workflows/${id}`);
  return res.data;
}

export async function createWorkflow(data: CreateWorkflowDto): Promise<Workflow> {
  const res = await api.post<Workflow>('/workflows', data);
  return res.data;
}

export async function updateWorkflow(id: string, data: UpdateWorkflowDto): Promise<Workflow> {
  const res = await api.put<Workflow>(`/workflows/${id}`, data);
  return res.data;
}

export async function deleteWorkflow(id: string): Promise<void> {
  await api.delete(`/workflows/${id}`);
}

export async function validateWorkflow(id: string): Promise<ValidateWorkflowResult> {
  const response = await api.post<ValidateWorkflowResult>('/workflows/validate', { workflowId: id });
  return response.data;
}

export async function confirmWorkflow(id: string): Promise<Workflow> {
  const response = await api.post<Workflow>(`/workflows/${id}/confirm`);
  return response.data;
}

export async function rejectDraft(id: string, reason: string): Promise<Workflow> {
  const response = await api.post<Workflow>(`/workflows/${id}/reject`, { reason });
  return response.data;
}

export async function executeWorkflow(workflowId: string): Promise<ExecutionDto> {
  const res = await api.post<ExecutionDto>(`/workflows/${workflowId}/execute`);
  return res.data;
}

export async function getExecution(executionId: string): Promise<ExecutionDto> {
  const res = await api.get<ExecutionDto>(`/executions/${executionId}`);
  return res.data;
}

/** 执行列表查询参数。 */
export interface ExecutionQuery {
  /** 状态过滤（字符串值，如 'Completed'/'Running'）。 */
  status?: string;
  /** 页码，从 1 开始。 */
  page?: number;
  /** 每页大小。 */
  pageSize?: number;
}

export async function getWorkflowExecutions(
  workflowId: string,
  query: ExecutionQuery = {},
): Promise<PagedResult<ExecutionSummaryDto>> {
  const res = await api.get<PagedResult<ExecutionSummaryDto>>(`/workflows/${workflowId}/executions`, { params: query });
  return res.data;
}

/** 获取指定工作流当前运行中的执行（待执行/执行中），供前端实时跟踪。 */
export async function getActiveExecutions(workflowId: string): Promise<ExecutionSummaryDto[]> {
  const res = await api.get<ExecutionSummaryDto[]>(`/workflows/${workflowId}/executions/active`);
  return res.data;
}

export async function cancelExecution(executionId: string): Promise<ExecutionDto> {
  const res = await api.post<ExecutionDto>(`/executions/${executionId}/cancel`);
  return res.data;
}

export async function getCredentials(): Promise<CredentialDto[]> {
  const res = await api.get<CredentialDto[]>('/credentials');
  return res.data;
}

export async function createCredential(data: CreateCredentialDto): Promise<CredentialDto> {
  const res = await api.post<CredentialDto>('/credentials', data);
  return res.data;
}

export async function getCredential(id: string): Promise<CredentialDto> {
  const res = await api.get<CredentialDto>(`/credentials/${id}`);
  return res.data;
}

export async function updateCredential(id: string, data: UpdateCredentialDto): Promise<CredentialDto> {
  const res = await api.put<CredentialDto>(`/credentials/${id}`, data);
  return res.data;
}

export async function deleteCredential(id: string): Promise<void> {
  await api.delete(`/credentials/${id}`);
}

export async function dryRun(request: DryRunRequest): Promise<ExecutionDto> {
  const res = await api.post<ExecutionDto>('/workflows/dry-run', request);
  return res.data;
}

export async function getCredentialTypes(): Promise<CredentialTypeDefinition[]> {
  const res = await api.get<CredentialTypeDefinition[]>('/credentials/types');
  return res.data;
}

// --- Triggers ---

export async function getTriggers(workflowId: string): Promise<TriggerDto[]> {
  const res = await api.get<TriggerDto[]>('/triggers', { params: { workflowDefinitionId: workflowId } });
  return res.data;
}

export async function createTrigger(workflowId: string, data: CreateTriggerDto): Promise<TriggerDto> {
  const res = await api.post<TriggerDto>('/triggers', { ...data, workflowDefinitionId: workflowId });
  return res.data;
}

export async function updateTrigger(_workflowId: string, triggerId: string, data: UpdateTriggerDto): Promise<TriggerDto> {
  const res = await api.put<TriggerDto>(`/triggers/${triggerId}`, data);
  return res.data;
}

export async function deleteTrigger(_workflowId: string, triggerId: string): Promise<void> {
  await api.delete(`/triggers/${triggerId}`);
}

// -- Auth --

export async function login(data: LoginRequest): Promise<LoginResult> {
  const res = await api.post<LoginResult>('/auth/login', data);
  return res.data;
}

export async function logout(): Promise<void> {
  await api.post('/auth/logout');
}

export async function getCurrentUser(): Promise<UserDto> {
  const res = await api.get<UserDto>('/auth/me');
  return res.data;
}

// --- Projects ---

export async function getProjects(): Promise<ProjectDto[]> {
  const res = await api.get<{ items: ProjectDto[] }>('/projects');
  return res.data.items;
}

export async function getProject(id: string): Promise<ProjectDto> {
  const res = await api.get<ProjectDto>(`/projects/${id}`);
  return res.data;
}

export async function createProject(data: CreateProjectDto): Promise<ProjectDto> {
  const res = await api.post<ProjectDto>('/projects', data);
  return res.data;
}

export async function updateProject(id: string, data: UpdateProjectDto): Promise<ProjectDto> {
  const res = await api.put<ProjectDto>(`/projects/${id}`, data);
  return res.data;
}

export async function deleteProject(id: string): Promise<void> {
  await api.delete(`/projects/${id}`);
}

// --- Workflow Import/Export ---

export async function exportWorkflow(id: string): Promise<WorkflowExportResult> {
  const res = await api.get<WorkflowExportResult>(`/workflows/${id}/export`);
  return res.data;
}

export async function exportWorkflowsBatch(ids: string[]): Promise<WorkflowExportResult[]> {
  const res = await api.post<WorkflowExportResult[]>('/workflows/export-batch', { ids } satisfies ExportBatchRequest);
  return res.data;
}

export async function importWorkflow(data: ImportWorkflowRequest): Promise<ImportResult> {
  const res = await api.post<ImportResult>('/workflows/import', data);
  return res.data;
}

export async function importWorkflowsBatch(data: ImportBatchRequest): Promise<BatchImportResult> {
  const res = await api.post<BatchImportResult>('/workflows/import-batch', data);
  return res.data;
}

// --- API Keys (Personal Access Tokens) ---

export async function createApiKey(name: string, expiresAt?: string | null): Promise<CreateApiKeyResult> {
  const res = await api.post<CreateApiKeyResult>('/auth/api-keys', { name, expiresAt });
  return res.data;
}

/** API Key list item DTO (returned by listApiKeys, no plaintext key). */
export interface ApiKeyDto {
  id: string;
  name: string;
  prefix: string;
  createdAt: string;
  expiresAt: string | null;
  revokedAt: string | null;
}

export async function listApiKeys(): Promise<ApiKeyDto[]> {
  const res = await api.get<ApiKeyDto[]>('/auth/api-keys');
  return res.data;
}

export async function revokeApiKey(id: string): Promise<void> {
  await api.delete(`/auth/api-keys/${id}`);
}

// --- User Roles ---

export async function getUserRoles(userId: string): Promise<string[]> {
  const res = await api.get<string[]>(`/users/${userId}/roles`);
  return res.data;
}

export async function assignRole(userId: string, role: string): Promise<void> {
  await api.post(`/users/${userId}/roles`, { role });
}

export async function revokeRole(userId: string, role: string): Promise<void> {
  await api.delete(`/users/${userId}/roles/${role}`);
}

// --- Audit Events ---

export interface AuditQueryParams {
  eventType?: string;
  from?: string;
  to?: string;
  resourceType?: string;
  resourceId?: string;
  offset: number;
  limit: number;
}

export interface AuditQueryResult {
  total: number;
  offset: number;
  limit: number;
  events: Record<string, unknown>[];
}

export async function queryAuditEvents(params: AuditQueryParams): Promise<AuditQueryResult> {
  const res = await api.get<AuditQueryResult>('/audit-events', { params });
  return res.data;
}

// --- File Storage ---

export interface StoredFileDto {
  id: string;
  fileName: string;
  contentType: string;
  fileSize: number;
  createdAt: string;
}

export interface UploadFileResult {
  id: string;
  fileName: string;
  fileSize: number;
}

export async function uploadFile(file: File, projectId: string): Promise<UploadFileResult> {
  const formData = new FormData();
  formData.append('file', file);
  const res = await api.post<UploadFileResult>(`/files/upload?projectId=${projectId}`, formData, {
    headers: { 'Content-Type': 'multipart/form-data' },
  });
  return res.data;
}

export async function listFiles(projectId: string): Promise<StoredFileDto[]> {
  const res = await api.get<{ items: StoredFileDto[] }>('/files', { params: { projectId } });
  return res.data.items;
}

export async function downloadFile(id: string): Promise<Blob> {
  const res = await api.get<Blob>(`/files/${id}/download`, { responseType: 'blob' });
  return res.data;
}

export async function deleteFile(id: string): Promise<void> {
  await api.delete(`/files/${id}`);
}

/** Format file size in human-readable form */
export function formatFileSize(bytes: number): string {
  if (bytes === 0) return '0 B';
  const units = ['B', 'KB', 'MB', 'GB'];
  const k = 1024;
  const i = Math.floor(Math.log(bytes) / Math.log(k));
  return `${parseFloat((bytes / Math.pow(k, i)).toFixed(1))} ${units[i]}`;
}

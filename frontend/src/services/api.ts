import axios from 'axios';
import { tokenStore } from '../utils/tokenStore.ts';
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
  RegisterRequest,
  RegisterResult,
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
} from '../types/workflow.ts';

const api = axios.create({
  baseURL: '/api/v1',
  headers: { 'Content-Type': 'application/json' },
  withCredentials: true,
});

api.interceptors.request.use((config) => {
  const token = tokenStore.getToken();
  if (token && config.headers) {
    config.headers.Authorization = `Bearer ${token}`;
  }
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
      const code = data?.code ?? data?.type;

      if (status === 401) {
        tokenStore.clear();
        localStorage.removeItem('auth_user');
        window.location.href = '/login';
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

export async function executeWorkflow(workflowId: string): Promise<ExecutionDto> {
  const res = await api.post<ExecutionDto>(`/workflows/${workflowId}/execute`);
  return res.data;
}

export async function getExecution(executionId: string): Promise<ExecutionDto> {
  const res = await api.get<ExecutionDto>(`/executions/${executionId}`);
  return res.data;
}

export async function getWorkflowExecutions(workflowId: string): Promise<ExecutionSummaryDto[]> {
  const res = await api.get<ExecutionSummaryDto[]>(`/workflows/${workflowId}/executions`);
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

// --- Auth ---

export async function register(data: RegisterRequest): Promise<RegisterResult> {
  const res = await api.post<RegisterResult>('/auth/register', data);
  return res.data;
}

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
  const res = await api.get<ProjectDto[]>('/projects');
  return res.data;
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

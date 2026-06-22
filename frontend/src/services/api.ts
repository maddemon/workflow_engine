import axios from 'axios';
import type {
  NodeTypeDescriptor,
  Workflow,
  WorkflowSummary,
  CreateWorkflowDto,
  UpdateWorkflowDto,
  ExecutionDto,
  CredentialDto,
  CreateCredentialDto,
  UpdateCredentialDto,
  TriggerDto,
  CreateTriggerDto,
  UpdateTriggerDto,
  RegisterRequest,
  RegisterResult,
  LoginRequest,
  LoginResult,
  UserDto,
} from '../types/workflow.ts';

const api = axios.create({
  baseURL: '/api/v1',
  headers: { 'Content-Type': 'application/json' },
});

api.interceptors.request.use((config) => {
  const token = localStorage.getItem('auth_token');
  if (token && config.headers) {
    config.headers.Authorization = `Bearer ${token}`;
  }
  return config;
});

api.interceptors.response.use(
  (response) => response,
  (error) => {
    if (error.response?.status === 401) {
      localStorage.removeItem('auth_token');
      localStorage.removeItem('auth_user');
      window.location.href = '/login';
    }
    return Promise.reject(error);
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

export async function getWorkflowExecutions(workflowId: string): Promise<ExecutionDto[]> {
  const res = await api.get<ExecutionDto[]>(`/workflows/${workflowId}/executions`);
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

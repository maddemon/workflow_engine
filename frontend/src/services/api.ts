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
} from '../types/workflow.ts';

const api = axios.create({
  baseURL: '/api/v1',
  headers: { 'Content-Type': 'application/json' },
});

export async function getNodeTypes(category?: string): Promise<NodeTypeDescriptor[]> {
  const params = category ? { category } : {};
  const res = await api.get<NodeTypeDescriptor[]>('/node-types', { params });
  return res.data;
}

export async function getWorkflows(): Promise<WorkflowSummary[]> {
  const res = await api.get<WorkflowSummary[]>('/workflows');
  return res.data;
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

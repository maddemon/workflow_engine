import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import type {
  NodeTypeDescriptor,
  Workflow,
  WorkflowSummary,
  CreateWorkflowDto,
  UpdateWorkflowDto,
  ExecutionDto,
  ExecutionSummaryDto,
  CredentialDto,
  CreateCredentialDto,
  UpdateCredentialDto,
  TriggerDto,
  CreateTriggerDto,
  UpdateTriggerDto,
  LoginRequest,
  UserDto,
  ProjectDto,
  CreateProjectDto,
  UpdateProjectDto,
  WorkflowExportResult,
  ImportResult,
  BatchImportResult,
  ValidateWorkflowResult,
  CreateApiKeyResult,
} from '../../types/workflow.ts';
import type { StoredFileDto } from '../api.ts';

const { mockedCreate, requestInterceptors, responseSuccessInterceptors, responseErrorInterceptors } = vi.hoisted(() => {
  const requestInterceptors: Array<(config: unknown) => unknown> = [];
  const responseSuccessInterceptors: Array<(response: unknown) => unknown> = [];
  const responseErrorInterceptors: Array<(error: unknown) => unknown> = [];
  const mockedCreate = {
    get: vi.fn(),
    post: vi.fn(),
    put: vi.fn(),
    delete: vi.fn(),
    interceptors: {
      request: {
        use: vi.fn((callback: (config: unknown) => unknown) => {
          requestInterceptors.push(callback);
          return 0;
        }),
      },
      response: {
        use: vi.fn(
          (success: (response: unknown) => unknown, error: (error: unknown) => unknown) => {
            responseSuccessInterceptors.push(success);
            responseErrorInterceptors.push(error);
            return 0;
          },
        ),
      },
    },
    defaults: { headers: { set: vi.fn() } },
  };
  return { mockedCreate, requestInterceptors, responseSuccessInterceptors, responseErrorInterceptors };
});

vi.mock('axios', () => ({
  default: {
    create: vi.fn(() => mockedCreate),
    interceptors: {
      request: { use: vi.fn() },
      response: { use: vi.fn() },
    },
  },
}));

const api = await import('../api.ts');

describe('api service', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  describe('ApiError', () => {
    it('constructs with status message code and details', () => {
      const err = new api.ApiError(404, 'Not found', 'NOT_FOUND', { detail: 'x' });
      expect(err.name).toBe('ApiError');
      expect(err.status).toBe(404);
      expect(err.message).toBe('Not found');
      expect(err.code).toBe('NOT_FOUND');
      expect(err.details).toEqual({ detail: 'x' });
    });
  });

  describe('interceptors', () => {
    const originalLocation = window.location;
    const originalLocalStorage = window.localStorage;

    beforeEach(() => {
      const store: Record<string, string> = {};
      Object.defineProperty(window, 'localStorage', {
        writable: true,
        value: {
          getItem: (key: string) => store[key] ?? null,
          setItem: (key: string, value: string) => { store[key] = value; },
          removeItem: (key: string) => { delete store[key]; },
        },
      });
    });

    afterEach(() => {
      Object.defineProperty(window, 'location', {
        writable: true,
        value: originalLocation,
      });
      Object.defineProperty(window, 'localStorage', {
        writable: true,
        value: originalLocalStorage,
      });
    });

    it('request interceptor sets Accept-Language header', () => {
      const config = { headers: { set: vi.fn() } };
      const callback = requestInterceptors[0];
      const result = callback(config);
      expect(config.headers.set).toHaveBeenCalledWith('Accept-Language', expect.any(String));
      expect(result).toBe(config);
    });

    it('response success interceptor returns response unchanged', () => {
      const response = { data: {} };
      const callback = responseSuccessInterceptors[0];
      expect(callback(response)).toBe(response);
    });

    it('response error interceptor builds ApiError from response data', async () => {
      const callback = responseErrorInterceptors[0];
      const error = {
        response: {
          status: 400,
          data: { message: 'bad request', errorCode: 'ERR' },
        },
        message: 'network fail',
      };
      await expect(callback(error)).rejects.toBeInstanceOf(api.ApiError);
      await expect(callback(error)).rejects.toMatchObject({
        status: 400,
        message: 'bad request',
        code: 'ERR',
      });
    });

    it('response error interceptor falls back to error message when data lacks message', async () => {
      const callback = responseErrorInterceptors[0];
      const error = {
        response: {
          status: 500,
          data: { title: 'server error' },
        },
        message: 'original',
      };
      await expect(callback(error)).rejects.toMatchObject({
        status: 500,
        message: 'server error',
      });
    });

    it('response error interceptor handles network errors with status 0', async () => {
      const callback = responseErrorInterceptors[0];
      const error = { message: 'Network unreachable' };
      await expect(callback(error)).rejects.toMatchObject({
        status: 0,
        message: 'Network unreachable',
      });
    });

    it('response 401 redirects to login when not on login page', async () => {
      const originalHref = window.location.href;
      Object.defineProperty(window, 'location', {
        writable: true,
        value: { pathname: '/dashboard', href: '/dashboard' },
      });
      const callback = responseErrorInterceptors[0];
      const error = {
        response: { status: 401, data: { message: 'unauthorized' } },
        message: 'unauthorized',
      };
      await expect(callback(error)).rejects.toMatchObject({ status: 401 });
      expect(window.location.href).toBe('/login');
      Object.defineProperty(window, 'location', {
        writable: true,
        value: { pathname: '/login', href: originalHref },
      });
    });

    it('response 401 on login page does not redirect', async () => {
      Object.defineProperty(window, 'location', {
        writable: true,
        value: { pathname: '/login', href: '/login' },
      });
      const callback = responseErrorInterceptors[0];
      const error = {
        response: { status: 401, data: { message: 'unauthorized' } },
        message: 'unauthorized',
      };
      await expect(callback(error)).rejects.toMatchObject({ status: 401 });
      expect(window.location.href).toBe('/login');
    });
  });

  describe('node types', () => {
    it('getNodeTypes returns node types without category', async () => {
      const items: NodeTypeDescriptor[] = [];
      mockedCreate.get.mockResolvedValue({ data: items });
      const result = await api.getNodeTypes();
      expect(result).toBe(items);
      expect(mockedCreate.get).toHaveBeenCalledWith('/node-types', { params: {} });
    });

    it('getNodeTypes passes category param when provided', async () => {
      mockedCreate.get.mockResolvedValue({ data: [] });
      await api.getNodeTypes('Http');
      expect(mockedCreate.get).toHaveBeenCalledWith('/node-types', { params: { category: 'Http' } });
    });
  });

  describe('workflows', () => {
    it('getWorkflows returns items array', async () => {
      const items: WorkflowSummary[] = [{ id: '1', name: 'w', version: 1, isActive: false, projectId: null, createdAt: '', updatedAt: null, lastExecutionAt: null, triggerCount: 0, nextTriggerAt: null }];
      mockedCreate.get.mockResolvedValue({ data: { items, totalCount: 1 } });
      const result = await api.getWorkflows();
      expect(result).toEqual(items);
    });

    it('getWorkflow returns workflow by id', async () => {
      const workflow = { id: '1' } as unknown as Workflow;
      mockedCreate.get.mockResolvedValue({ data: workflow });
      const result = await api.getWorkflow('1');
      expect(result).toBe(workflow);
      expect(mockedCreate.get).toHaveBeenCalledWith('/workflows/1');
    });

    it('createWorkflow posts data and returns workflow', async () => {
      const workflow = { id: '1' } as unknown as Workflow;
      const dto: CreateWorkflowDto = { name: 'w', createdBy: 'u', nodes: [], connections: [] };
      mockedCreate.post.mockResolvedValue({ data: workflow });
      const result = await api.createWorkflow(dto);
      expect(result).toBe(workflow);
      expect(mockedCreate.post).toHaveBeenCalledWith('/workflows', dto);
    });

    it('updateWorkflow puts data', async () => {
      const workflow = { id: '1' } as unknown as Workflow;
      const dto: UpdateWorkflowDto = { name: 'w', isActive: true, styleSettings: null, nodes: [], connections: [] };
      mockedCreate.put.mockResolvedValue({ data: workflow });
      const result = await api.updateWorkflow('1', dto);
      expect(result).toBe(workflow);
      expect(mockedCreate.put).toHaveBeenCalledWith('/workflows/1', dto);
    });

    it('deleteWorkflow calls delete', async () => {
      mockedCreate.delete.mockResolvedValue({});
      await api.deleteWorkflow('1');
      expect(mockedCreate.delete).toHaveBeenCalledWith('/workflows/1');
    });

    it('validateWorkflow posts workflowId', async () => {
      const res: ValidateWorkflowResult = { valid: true, errors: [], canAutoFix: false };
      mockedCreate.post.mockResolvedValue({ data: res });
      const result = await api.validateWorkflow('1');
      expect(result).toEqual(res);
      expect(mockedCreate.post).toHaveBeenCalledWith('/workflows/validate', { workflowId: '1' });
    });

    it('confirmWorkflow posts confirm endpoint', async () => {
      const workflow = { id: '1' } as unknown as Workflow;
      mockedCreate.post.mockResolvedValue({ data: workflow });
      const result = await api.confirmWorkflow('1');
      expect(result).toBe(workflow);
      expect(mockedCreate.post).toHaveBeenCalledWith('/workflows/1/confirm');
    });

    it('rejectDraft posts reason', async () => {
      const workflow = { id: '1' } as unknown as Workflow;
      mockedCreate.post.mockResolvedValue({ data: workflow });
      const result = await api.rejectDraft('1', 'bad');
      expect(result).toBe(workflow);
      expect(mockedCreate.post).toHaveBeenCalledWith('/workflows/1/reject', { reason: 'bad' });
    });

    it('executeWorkflow posts execute endpoint', async () => {
      const execution = { id: 'e1' } as unknown as ExecutionDto;
      mockedCreate.post.mockResolvedValue({ data: execution });
      const result = await api.executeWorkflow('1');
      expect(result).toBe(execution);
      expect(mockedCreate.post).toHaveBeenCalledWith('/workflows/1/execute');
    });

    it('getExecution returns execution', async () => {
      const execution = { id: 'e1' } as unknown as ExecutionDto;
      mockedCreate.get.mockResolvedValue({ data: execution });
      const result = await api.getExecution('e1');
      expect(result).toBe(execution);
      expect(mockedCreate.get).toHaveBeenCalledWith('/executions/e1');
    });

    it('getWorkflowExecutions returns paged executions', async () => {
      const paged = { items: [], totalCount: 0, page: 1, pageSize: 20, totalPages: 0 };
      mockedCreate.get.mockResolvedValue({ data: paged });
      const result = await api.getWorkflowExecutions('1');
      expect(result).toBe(paged);
      expect(mockedCreate.get).toHaveBeenCalledWith('/workflows/1/executions', { params: {} });
    });

    it('getActiveExecutions returns active executions', async () => {
      const items: ExecutionSummaryDto[] = [];
      mockedCreate.get.mockResolvedValue({ data: items });
      const result = await api.getActiveExecutions('1');
      expect(result).toBe(items);
      expect(mockedCreate.get).toHaveBeenCalledWith('/workflows/1/executions/active');
    });

    it('cancelExecution posts cancel endpoint', async () => {
      const execution = { id: 'e1' } as unknown as ExecutionDto;
      mockedCreate.post.mockResolvedValue({ data: execution });
      const result = await api.cancelExecution('e1');
      expect(result).toBe(execution);
      expect(mockedCreate.post).toHaveBeenCalledWith('/executions/e1/cancel');
    });

    it('dryRun posts dry-run endpoint', async () => {
      const execution = { id: 'e1' } as unknown as ExecutionDto;
      mockedCreate.post.mockResolvedValue({ data: execution });
      const result = await api.dryRun({ nodes: [], connections: [] });
      expect(result).toBe(execution);
      expect(mockedCreate.post).toHaveBeenCalledWith('/workflows/dry-run', { nodes: [], connections: [] });
    });
  });

  describe('credentials', () => {
    it('getCredentials returns credentials', async () => {
      const items: CredentialDto[] = [];
      mockedCreate.get.mockResolvedValue({ data: items });
      const result = await api.getCredentials();
      expect(result).toBe(items);
      expect(mockedCreate.get).toHaveBeenCalledWith('/credentials');
    });

    it('createCredential posts credential', async () => {
      const credential = { id: 'c1' } as unknown as CredentialDto;
      const dto: CreateCredentialDto = { name: 'n', type: 'apiKey', fields: {} };
      mockedCreate.post.mockResolvedValue({ data: credential });
      const result = await api.createCredential(dto);
      expect(result).toBe(credential);
      expect(mockedCreate.post).toHaveBeenCalledWith('/credentials', dto);
    });

    it('getCredential returns credential', async () => {
      const credential = { id: 'c1' } as unknown as CredentialDto;
      mockedCreate.get.mockResolvedValue({ data: credential });
      const result = await api.getCredential('c1');
      expect(result).toBe(credential);
      expect(mockedCreate.get).toHaveBeenCalledWith('/credentials/c1');
    });

    it('updateCredential puts credential', async () => {
      const credential = { id: 'c1' } as unknown as CredentialDto;
      const dto: UpdateCredentialDto = { name: 'n', fields: {} };
      mockedCreate.put.mockResolvedValue({ data: credential });
      const result = await api.updateCredential('c1', dto);
      expect(result).toBe(credential);
      expect(mockedCreate.put).toHaveBeenCalledWith('/credentials/c1', dto);
    });

    it('deleteCredential calls delete', async () => {
      mockedCreate.delete.mockResolvedValue({});
      await api.deleteCredential('c1');
      expect(mockedCreate.delete).toHaveBeenCalledWith('/credentials/c1');
    });

    it('getCredentialTypes returns types', async () => {
      mockedCreate.get.mockResolvedValue({ data: [] });
      const result = await api.getCredentialTypes();
      expect(result).toEqual([]);
      expect(mockedCreate.get).toHaveBeenCalledWith('/credentials/types');
    });
  });

  describe('triggers', () => {
    it('getTriggers returns triggers for workflow', async () => {
      mockedCreate.get.mockResolvedValue({ data: [] });
      const result = await api.getTriggers('1');
      expect(result).toEqual([]);
      expect(mockedCreate.get).toHaveBeenCalledWith('/triggers', { params: { workflowDefinitionId: '1' } });
    });

    it('createTrigger posts trigger with workflow id', async () => {
      const trigger = { id: 't1' } as unknown as TriggerDto;
      const dto: CreateTriggerDto = { workflowDefinitionId: '1', workflowVersion: 1, type: 'Schedule', name: 's' };
      mockedCreate.post.mockResolvedValue({ data: trigger });
      const result = await api.createTrigger('1', dto);
      expect(result).toBe(trigger);
      expect(mockedCreate.post).toHaveBeenCalledWith('/triggers', { ...dto, workflowDefinitionId: '1' });
    });

    it('updateTrigger puts trigger', async () => {
      const trigger = { id: 't1' } as unknown as TriggerDto;
      const dto: UpdateTriggerDto = { name: 's', isActive: true };
      mockedCreate.put.mockResolvedValue({ data: trigger });
      const result = await api.updateTrigger('1', 't1', dto);
      expect(result).toBe(trigger);
      expect(mockedCreate.put).toHaveBeenCalledWith('/triggers/t1', dto);
    });

    it('deleteTrigger calls delete', async () => {
      mockedCreate.delete.mockResolvedValue({});
      await api.deleteTrigger('1', 't1');
      expect(mockedCreate.delete).toHaveBeenCalledWith('/triggers/t1');
    });
  });

  describe('auth', () => {
    it('login posts credentials', async () => {
      const res: LoginRequest = { email: 'a@b.com', password: 'p' };
      mockedCreate.post.mockResolvedValue({ data: res });
      const result = await api.login(res);
      expect(result).toBe(res);
      expect(mockedCreate.post).toHaveBeenCalledWith('/auth/login', res);
    });

    it('logout posts logout', async () => {
      mockedCreate.post.mockResolvedValue({});
      await api.logout();
      expect(mockedCreate.post).toHaveBeenCalledWith('/auth/logout');
    });

    it('getCurrentUser returns user', async () => {
      const user = { id: '1' } as unknown as UserDto;
      mockedCreate.get.mockResolvedValue({ data: user });
      const result = await api.getCurrentUser();
      expect(result).toBe(user);
      expect(mockedCreate.get).toHaveBeenCalledWith('/auth/me');
    });
  });

  describe('projects', () => {
    it('getProjects returns items', async () => {
      const items: ProjectDto[] = [];
      mockedCreate.get.mockResolvedValue({ data: { items } });
      const result = await api.getProjects();
      expect(result).toBe(items);
      expect(mockedCreate.get).toHaveBeenCalledWith('/projects');
    });

    it('getProject returns project', async () => {
      const project = { id: '1' } as unknown as ProjectDto;
      mockedCreate.get.mockResolvedValue({ data: project });
      const result = await api.getProject('1');
      expect(result).toBe(project);
      expect(mockedCreate.get).toHaveBeenCalledWith('/projects/1');
    });

    it('createProject posts project', async () => {
      const project = { id: '1' } as unknown as ProjectDto;
      const dto: CreateProjectDto = { name: 'p' };
      mockedCreate.post.mockResolvedValue({ data: project });
      const result = await api.createProject(dto);
      expect(result).toBe(project);
      expect(mockedCreate.post).toHaveBeenCalledWith('/projects', dto);
    });

    it('updateProject puts project', async () => {
      const project = { id: '1' } as unknown as ProjectDto;
      const dto: UpdateProjectDto = { name: 'p' };
      mockedCreate.put.mockResolvedValue({ data: project });
      const result = await api.updateProject('1', dto);
      expect(result).toBe(project);
      expect(mockedCreate.put).toHaveBeenCalledWith('/projects/1', dto);
    });

    it('deleteProject calls delete', async () => {
      mockedCreate.delete.mockResolvedValue({});
      await api.deleteProject('1');
      expect(mockedCreate.delete).toHaveBeenCalledWith('/projects/1');
    });
  });

  describe('import/export', () => {
    it('exportWorkflow returns result', async () => {
      const res = { name: 'w' } as unknown as WorkflowExportResult;
      mockedCreate.get.mockResolvedValue({ data: res });
      const result = await api.exportWorkflow('1');
      expect(result).toBe(res);
      expect(mockedCreate.get).toHaveBeenCalledWith('/workflows/1/export');
    });

    it('exportWorkflowsBatch posts ids', async () => {
      const res: WorkflowExportResult[] = [];
      mockedCreate.post.mockResolvedValue({ data: res });
      const result = await api.exportWorkflowsBatch(['1', '2']);
      expect(result).toBe(res);
      expect(mockedCreate.post).toHaveBeenCalledWith('/workflows/export-batch', { ids: ['1', '2'] });
    });

    it('importWorkflow posts json', async () => {
      const res: ImportResult = { success: true, errors: [] };
      mockedCreate.post.mockResolvedValue({ data: res });
      const result = await api.importWorkflow({ json: '{}', importedBy: 'u' });
      expect(result).toBe(res);
      expect(mockedCreate.post).toHaveBeenCalledWith('/workflows/import', { json: '{}', importedBy: 'u' });
    });

    it('importWorkflowsBatch posts batch', async () => {
      const res: BatchImportResult = { successCount: 0, failureCount: 0, results: [] };
      mockedCreate.post.mockResolvedValue({ data: res });
      const result = await api.importWorkflowsBatch({ json: '[]', importedBy: 'u' });
      expect(result).toBe(res);
      expect(mockedCreate.post).toHaveBeenCalledWith('/workflows/import-batch', { json: '[]', importedBy: 'u' });
    });
  });

  describe('api keys', () => {
    it('createApiKey posts name and expiresAt', async () => {
      const res = { id: 'k1' } as unknown as CreateApiKeyResult;
      mockedCreate.post.mockResolvedValue({ data: res });
      const result = await api.createApiKey('mykey', '2026-01-01');
      expect(result).toBe(res);
      expect(mockedCreate.post).toHaveBeenCalledWith('/auth/api-keys', { name: 'mykey', expiresAt: '2026-01-01' });
    });

    it('listApiKeys returns keys', async () => {
      mockedCreate.get.mockResolvedValue({ data: [] });
      const result = await api.listApiKeys();
      expect(result).toEqual([]);
      expect(mockedCreate.get).toHaveBeenCalledWith('/auth/api-keys');
    });

    it('revokeApiKey calls delete', async () => {
      mockedCreate.delete.mockResolvedValue({});
      await api.revokeApiKey('k1');
      expect(mockedCreate.delete).toHaveBeenCalledWith('/auth/api-keys/k1');
    });
  });

  describe('users', () => {
    it('getUserRoles returns roles', async () => {
      mockedCreate.get.mockResolvedValue({ data: ['Admin'] });
      const result = await api.getUserRoles('1');
      expect(result).toEqual(['Admin']);
      expect(mockedCreate.get).toHaveBeenCalledWith('/users/1/roles');
    });

    it('assignRole posts role', async () => {
      mockedCreate.post.mockResolvedValue({});
      await api.assignRole('1', 'Admin');
      expect(mockedCreate.post).toHaveBeenCalledWith('/users/1/roles', { role: 'Admin' });
    });

    it('revokeRole calls delete', async () => {
      mockedCreate.delete.mockResolvedValue({});
      await api.revokeRole('1', 'Admin');
      expect(mockedCreate.delete).toHaveBeenCalledWith('/users/1/roles/Admin');
    });
  });

  describe('audit', () => {
    it('queryAuditEvents returns result', async () => {
      const res = { total: 0, offset: 0, limit: 10, events: [] };
      mockedCreate.get.mockResolvedValue({ data: res });
      const result = await api.queryAuditEvents({ offset: 0, limit: 10 });
      expect(result).toBe(res);
      expect(mockedCreate.get).toHaveBeenCalledWith('/audit-events', { params: { offset: 0, limit: 10 } });
    });
  });

  describe('files', () => {
    it('uploadFile posts form data', async () => {
      const file = new File(['x'], 'test.txt');
      const res = { id: 'f1', fileName: 'test.txt', fileSize: 1 };
      mockedCreate.post.mockResolvedValue({ data: res });
      const result = await api.uploadFile(file, 'p1');
      expect(result).toBe(res);
      expect(mockedCreate.post).toHaveBeenCalledWith(
        '/files/upload?projectId=p1',
        expect.any(FormData),
        { headers: { 'Content-Type': 'multipart/form-data' } },
      );
    });

    it('listFiles returns items', async () => {
      const items: StoredFileDto[] = [];
      mockedCreate.get.mockResolvedValue({ data: { items } });
      const result = await api.listFiles('p1');
      expect(result).toBe(items);
      expect(mockedCreate.get).toHaveBeenCalledWith('/files', { params: { projectId: 'p1' } });
    });

    it('downloadFile returns blob', async () => {
      const blob = new Blob(['x']);
      mockedCreate.get.mockResolvedValue({ data: blob });
      const result = await api.downloadFile('f1');
      expect(result).toBe(blob);
      expect(mockedCreate.get).toHaveBeenCalledWith('/files/f1/download', { responseType: 'blob' });
    });

    it('deleteFile calls delete', async () => {
      mockedCreate.delete.mockResolvedValue({});
      await api.deleteFile('f1');
      expect(mockedCreate.delete).toHaveBeenCalledWith('/files/f1');
    });
  });

  describe('formatFileSize', () => {
    it('returns 0 B for zero bytes', () => {
      expect(api.formatFileSize(0)).toBe('0 B');
    });

    it('returns KB for small files', () => {
      expect(api.formatFileSize(1024)).toBe('1 KB');
    });

    it('returns MB for larger files', () => {
      expect(api.formatFileSize(1024 * 1024)).toBe('1 MB');
    });

    it('returns GB for very large files', () => {
      expect(api.formatFileSize(1024 * 1024 * 1024)).toBe('1 GB');
    });
  });
});

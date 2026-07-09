import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import axios, { type AxiosInstance } from 'axios';
import { mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { tmpdir } from 'node:os';
import {
  workflowCreate,
  workflowDelete,
  workflowExport,
  workflowGet,
  workflowImport,
  workflowList,
  workflowUpdate,
  workflowValidate,
  workflowVersions,
} from '../commands/workflows.js';
import { setProfile, type ConfigOptions } from '../config.js';
import { CLIError, ErrorCode, ExitCode } from '../errors.js';
import { setOutputOptions } from '../output.js';

vi.mock('axios', async (importOriginal) => {
  const actual = await importOriginal<typeof import('axios')>();
  return {
    ...actual,
    default: {
      ...actual.default,
      create: vi.fn(),
    },
  };
});

function captureStdout(callback: () => Promise<void>): Promise<string> {
  return new Promise((resolve, reject) => {
    const originalLog = console.log;
    const outputs: string[] = [];
    console.log = (message: string) => {
      outputs.push(message);
    };
    callback()
      .then(() => {
        console.log = originalLog;
        resolve(outputs.join('\n'));
      })
      .catch((err) => {
        console.log = originalLog;
        reject(err);
      });
  });
}

describe('commands/workflows', () => {
  let tempDir: string;
  let options: ConfigOptions;
  let mockInstance: {
    interceptors: {
      request: { use: ReturnType<typeof vi.fn> };
      response: { use: ReturnType<typeof vi.fn> };
    };
    get: ReturnType<typeof vi.fn>;
    post: ReturnType<typeof vi.fn>;
    put: ReturnType<typeof vi.fn>;
    delete: ReturnType<typeof vi.fn>;
  };

  beforeEach(() => {
    tempDir = mkdtempSync(join(tmpdir(), 'flowengine-cli-workflows-test-'));
    options = { configDir: tempDir };
    setOutputOptions({ json: false, verbose: false });

    mockInstance = {
      interceptors: {
        request: { use: vi.fn() },
        response: { use: vi.fn() },
      },
      get: vi.fn(),
      post: vi.fn(),
      put: vi.fn(),
      delete: vi.fn(),
    };
    vi.mocked(axios.create).mockReturnValue(mockInstance as unknown as AxiosInstance);

    setProfile(
      'default',
      {
        baseUrl: 'http://localhost:5000',
        token: 'test-token',
        userId: 'user-1',
        email: 'a@example.com',
      },
      options,
    );
  });

  afterEach(() => {
    rmSync(tempDir, { recursive: true, force: true });
    vi.restoreAllMocks();
  });

  it('list - returns workflow summaries', async () => {
    mockInstance.get.mockResolvedValue({
      data: {
        items: [
          {
            id: 'wf-1',
            name: 'Workflow One',
            version: 1,
            isActive: true,
            projectId: 'project-1',
            createdAt: '2025-01-01T00:00:00Z',
          },
        ],
        totalCount: 1,
      },
    });

    const output = await captureStdout(() => workflowList({ configOptions: options }));

    expect(output).toContain('wf-1: Workflow One (v1, active=true, Project: project-1)');
    expect(mockInstance.get).toHaveBeenCalledWith('/workflows', { params: {} });
  });

  it('list - supports pagination and project filter', async () => {
    mockInstance.get.mockResolvedValue({ data: { items: [], totalCount: 0 } });

    await workflowList({
      page: 2,
      pageSize: 10,
      projectId: 'project-1',
      configOptions: options,
    });

    expect(mockInstance.get).toHaveBeenCalledWith('/workflows', {
      params: { page: 2, pageSize: 10, projectId: 'project-1' },
    });
  });

  it('get - returns workflow details', async () => {
    mockInstance.get.mockResolvedValue({
      data: {
        id: 'wf-1',
        name: 'Workflow One',
        version: 1,
        isActive: true,
        createdBy: 'user-1',
        createdAt: '2025-01-01T00:00:00Z',
        nodes: [],
        connections: [],
      },
    });

    const output = await captureStdout(() => workflowGet({ id: 'wf-1', configOptions: options }));

    expect(output).toContain('ID: wf-1');
    expect(output).toContain('Version: 1');
    expect(mockInstance.get).toHaveBeenCalledWith('/workflows/wf-1');
  });

  it('get - fetches specific version', async () => {
    mockInstance.get.mockResolvedValue({
      data: { id: 'wf-1', name: 'Workflow One', version: 2, isActive: false, createdBy: 'user-1', createdAt: '2025-01-01T00:00:00Z', nodes: [], connections: [] },
    });

    await workflowGet({ id: 'wf-1', version: 2, configOptions: options });

    expect(mockInstance.get).toHaveBeenCalledWith('/workflows/wf-1/versions/2');
  });

  it('versions - returns version history', async () => {
    mockInstance.get.mockResolvedValue({
      data: [
        { id: 'wf-1', name: 'Workflow One', version: 1, isActive: false, createdBy: 'user-1', createdAt: '2025-01-01T00:00:00Z', nodes: [], connections: [] },
        { id: 'wf-1', name: 'Workflow One', version: 2, isActive: true, createdBy: 'user-1', createdAt: '2025-01-02T00:00:00Z', nodes: [], connections: [] },
      ],
    });

    const output = await captureStdout(() => workflowVersions({ id: 'wf-1', configOptions: options }));

    expect(output).toContain('v1: Workflow One (active=false)');
    expect(output).toContain('v2: Workflow One (active=true)');
  });

  it('create - posts CreateWorkflowDto from file', async () => {
    const filePath = join(tempDir, 'workflow.json');
    writeFileSync(
      filePath,
      JSON.stringify({ name: 'Workflow One', nodes: [], connections: [] }),
      'utf-8',
    );

    mockInstance.post.mockResolvedValue({
      data: { id: 'wf-1', name: 'Workflow One', version: 1, isActive: true, createdBy: 'user-1', createdAt: '2025-01-01T00:00:00Z', nodes: [], connections: [] },
    });

    await workflowCreate({ file: filePath, configOptions: options });

    expect(mockInstance.post).toHaveBeenCalledWith('/workflows', {
      name: 'Workflow One',
      createdBy: 'user-1',
      nodes: [],
      connections: [],
    });
  });

  it('create - CLI name overrides file name', async () => {
    const filePath = join(tempDir, 'workflow.json');
    writeFileSync(
      filePath,
      JSON.stringify({ name: 'Old Name', nodes: [], connections: [] }),
      'utf-8',
    );

    mockInstance.post.mockResolvedValue({
      data: { id: 'wf-1', name: 'New Name', version: 1, isActive: true, createdBy: 'user-1', createdAt: '2025-01-01T00:00:00Z', nodes: [], connections: [] },
    });

    await workflowCreate({ file: filePath, name: 'New Name', configOptions: options });

    expect(mockInstance.post.mock.calls[0][1].name).toBe('New Name');
  });

  it('create - dry-run prints request body for valid workflow', async () => {
    const filePath = join(tempDir, 'workflow.json');
    writeFileSync(
      filePath,
      JSON.stringify({
        name: 'Workflow One',
        nodes: [
          {
            id: 'start',
            typeName: 'manualTrigger',
            name: '开始',
            parameters: {},
            ports: [{ name: 'Output', direction: 'Output', type: 'Main' }],
            positionX: 100,
            positionY: 100,
            isEntry: true,
          },
          {
            id: 'http',
            typeName: 'httpRequest',
            name: '请求',
            parameters: { method: 'GET', url: 'https://api.example.com/items' },
            ports: [
              { name: 'Input', direction: 'Input', type: 'Main' },
              { name: 'Output', direction: 'Output', type: 'Main' },
            ],
            positionX: 300,
            positionY: 100,
            isEntry: false,
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
      }),
      'utf-8',
    );

    mockInstance.get.mockRejectedValue(new Error('offline'));

    const output = await captureStdout(() =>
      workflowCreate({ file: filePath, dryRun: true, configOptions: options }),
    );

    expect(output).toContain('Dry-run 模式');
    expect(output).toContain('"name": "Workflow One"');
    expect(output).toContain('"createdBy": "user-1"');
    expect(mockInstance.post).not.toHaveBeenCalled();
  });

  it('create - dry-run fails validation for invalid node type', async () => {
    const filePath = join(tempDir, 'invalid.json');
    writeFileSync(
      filePath,
      JSON.stringify({
        name: 'Invalid Workflow',
        nodes: [
          {
            id: 'start',
            typeName: 'manualTrigger',
            name: '开始',
            parameters: {},
            ports: [{ name: 'Output', direction: 'Output', type: 'Main' }],
            positionX: 100,
            positionY: 100,
            isEntry: true,
          },
          {
            id: 'bad',
            typeName: 'notImplementedNode',
            name: 'Bad',
            parameters: {},
            ports: [{ name: 'Output', direction: 'Output', type: 'Main' }],
            positionX: 300,
            positionY: 100,
            isEntry: false,
          },
        ],
        connections: [],
      }),
      'utf-8',
    );

    mockInstance.get.mockRejectedValue(new Error('offline'));

    await expect(
      workflowCreate({ file: filePath, dryRun: true, configOptions: options }),
    ).rejects.toThrow(CLIError);

    expect(mockInstance.post).not.toHaveBeenCalled();
  });

  it('create - uses email when userId missing', async () => {
    setProfile('default', { baseUrl: 'http://localhost:5000', token: 'test-token', email: 'a@example.com' }, options);
    const filePath = join(tempDir, 'workflow.json');
    writeFileSync(filePath, JSON.stringify({ name: 'Workflow One', nodes: [], connections: [] }), 'utf-8');

    mockInstance.post.mockResolvedValue({
      data: { id: 'wf-1', name: 'Workflow One', version: 1, isActive: true, createdBy: 'a@example.com', createdAt: '2025-01-01T00:00:00Z', nodes: [], connections: [] },
    });

    await workflowCreate({ file: filePath, configOptions: options });

    expect(mockInstance.post.mock.calls[0][1].createdBy).toBe('a@example.com');
  });

  it('create - rejects missing createdBy with InvocationError', async () => {
    setProfile('default', { baseUrl: 'http://localhost:5000', token: 'test-token' }, options);
    const filePath = join(tempDir, 'workflow.json');
    writeFileSync(filePath, JSON.stringify({ name: 'Workflow One', nodes: [], connections: [] }), 'utf-8');

    await expect(workflowCreate({ file: filePath, configOptions: options })).rejects.toThrow(CLIError);

    try {
      await workflowCreate({ file: filePath, configOptions: options });
    } catch (err) {
      const cliErr = err as CLIError;
      expect(cliErr.exitCode).toBe(ExitCode.InvocationError);
    }
  });

  it('update - puts UpdateWorkflowDto from file', async () => {
    const filePath = join(tempDir, 'workflow.json');
    writeFileSync(
      filePath,
      JSON.stringify({ name: 'Updated Name', isActive: false, nodes: [], connections: [] }),
      'utf-8',
    );

    mockInstance.put.mockResolvedValue({
      data: { id: 'wf-1', name: 'Updated Name', version: 2, isActive: false, createdBy: 'user-1', createdAt: '2025-01-01T00:00:00Z', nodes: [], connections: [] },
    });

    await workflowUpdate({ id: 'wf-1', file: filePath, configOptions: options });

    expect(mockInstance.put).toHaveBeenCalledWith('/workflows/wf-1', {
      name: 'Updated Name',
      isActive: false,
      nodes: [],
      connections: [],
    });
  });

  it('update - accepts active only', async () => {
    mockInstance.put.mockResolvedValue({
      data: { id: 'wf-1', name: 'wf-1', version: 2, isActive: true, createdBy: 'user-1', createdAt: '2025-01-01T00:00:00Z', nodes: [], connections: [] },
    });

    await workflowUpdate({ id: 'wf-1', active: 'true', configOptions: options });

    expect(mockInstance.put).toHaveBeenCalledWith('/workflows/wf-1', {
      name: 'wf-1',
      isActive: true,
      nodes: [],
      connections: [],
    });
  });

  it('update - rejects when no options provided', async () => {
    await expect(workflowUpdate({ id: 'wf-1', configOptions: options })).rejects.toThrow(CLIError);

    try {
      await workflowUpdate({ id: 'wf-1', configOptions: options });
    } catch (err) {
      const cliErr = err as CLIError;
      expect(cliErr.exitCode).toBe(ExitCode.InvocationError);
    }
  });

  it('delete - calls delete endpoint with confirm', async () => {
    mockInstance.delete.mockResolvedValue({ data: {} });

    const output = await captureStdout(() =>
      workflowDelete({ id: 'wf-1', confirm: true, configOptions: options }),
    );

    expect(output).toContain('已删除工作流：wf-1');
    expect(mockInstance.delete).toHaveBeenCalledWith('/workflows/wf-1');
  });

  it('export - outputs JSON to stdout', async () => {
    mockInstance.get.mockResolvedValue({
      data: { name: 'Workflow One', version: 1, nodes: [], connections: [], exportedAt: '2025-01-01T00:00:00Z', exportedBy: 'user-1' },
    });

    const output = await captureStdout(() => workflowExport({ id: 'wf-1', configOptions: options }));

    expect(output).toContain('"name": "Workflow One"');
    expect(mockInstance.get).toHaveBeenCalledWith('/workflows/wf-1/export');
  });

  it('export - writes to output file', async () => {
    mockInstance.get.mockResolvedValue({
      data: { name: 'Workflow One', version: 1, nodes: [], connections: [], exportedAt: '2025-01-01T00:00:00Z', exportedBy: 'user-1' },
    });

    const outputPath = join(tempDir, 'exported.json');
    await workflowExport({ id: 'wf-1', output: outputPath, configOptions: options });

    expect(mockInstance.get).toHaveBeenCalledWith('/workflows/wf-1/export');
  });

  it('import - posts JSON string', async () => {
    const filePath = join(tempDir, 'import.json');
    writeFileSync(filePath, JSON.stringify({ name: 'Imported', nodes: [], connections: [] }), 'utf-8');

    mockInstance.post.mockResolvedValue({
      data: { success: true, workflowId: 'wf-2', workflowName: 'Imported', errors: [] },
    });

    await workflowImport({ file: filePath, configOptions: options });

    const requestBody = mockInstance.post.mock.calls[0][1] as { json: string; importedBy: string };
    expect(requestBody.importedBy).toBe('user-1');
    expect(JSON.parse(requestBody.json).name).toBe('Imported');
  });

  it('import - dry-run prints request body', async () => {
    const filePath = join(tempDir, 'import.json');
    writeFileSync(filePath, JSON.stringify({ name: 'Imported', nodes: [], connections: [] }), 'utf-8');

    const output = await captureStdout(() =>
      workflowImport({ file: filePath, dryRun: true, configOptions: options }),
    );

    expect(output).toContain('Dry-run 模式');
    expect(output).toContain('"json"');
    expect(mockInstance.post).not.toHaveBeenCalled();
  });

  it('create - rejects missing name with InvocationError', async () => {
    const filePath = join(tempDir, 'workflow.json');
    writeFileSync(filePath, JSON.stringify({ nodes: [], connections: [] }), 'utf-8');

    await expect(workflowCreate({ file: filePath, configOptions: options })).rejects.toThrow(CLIError);

    try {
      await workflowCreate({ file: filePath, configOptions: options });
    } catch (err) {
      const cliErr = err as CLIError;
      expect(cliErr.exitCode).toBe(ExitCode.InvocationError);
    }
  });

  it('create - rejects unreadable file with InvocationError', async () => {
    await expect(
      workflowCreate({ file: join(tempDir, 'missing.json'), configOptions: options }),
    ).rejects.toThrow(CLIError);

    try {
      await workflowCreate({ file: join(tempDir, 'missing.json'), configOptions: options });
    } catch (err) {
      const cliErr = err as CLIError;
      expect(cliErr.exitCode).toBe(ExitCode.InvocationError);
    }
  });

  describe('workflow validate', () => {
    function writeWorkflow(fileName: string, workflow: unknown): string {
      const filePath = join(tempDir, fileName);
      writeFileSync(filePath, JSON.stringify(workflow), 'utf-8');
      return filePath;
    }

    const manualTriggerNode = {
      id: 'start',
      typeName: 'manualTrigger',
      name: '开始',
      parameters: {},
      ports: [{ name: 'Output', direction: 'Output', type: 'Main' }],
      positionX: 100,
      positionY: 100,
      isEntry: true,
    };

    const httpRequestNode = {
      id: 'http',
      typeName: 'httpRequest',
      name: '请求',
      parameters: { method: 'GET', url: 'https://api.example.com/items' },
      ports: [
        { name: 'Input', direction: 'Input', type: 'Main' },
        { name: 'Output', direction: 'Output', type: 'Main' },
      ],
      positionX: 300,
      positionY: 100,
      isEntry: false,
    };

    const validWorkflow = {
      name: 'Valid Workflow',
      nodes: [manualTriggerNode, httpRequestNode],
      connections: [
        {
          id: 'conn-1',
          sourceNodeId: 'start',
          sourcePortName: 'Output',
          targetNodeId: 'http',
          targetPortName: 'Input',
        },
      ],
    };

    beforeEach(() => {
      mockInstance.get.mockRejectedValue(new Error('offline'));
    });

    it('passes for valid workflow', async () => {
      const filePath = writeWorkflow('valid.json', validWorkflow);

      const output = await captureStdout(() =>
        workflowValidate({ file: filePath, configOptions: options }),
      );

      expect(output).toContain('工作流校验通过');
    });

    it('reports unknown node type', async () => {
      const workflow = {
        ...validWorkflow,
        nodes: [
          {
            id: 'bad',
            typeName: 'unknownNode',
            name: 'Bad',
            parameters: {},
            ports: [{ name: 'Output', direction: 'Output', type: 'Main' }],
            positionX: 0,
            positionY: 0,
            isEntry: true,
          },
        ],
      };
      const filePath = writeWorkflow('unknown.json', workflow);

      await expect(
        workflowValidate({ file: filePath, configOptions: options }),
      ).rejects.toThrow(CLIError);

      try {
        await workflowValidate({ file: filePath, configOptions: options });
      } catch (err) {
        const cliErr = err as CLIError;
        expect(cliErr.exitCode).toBe(ExitCode.InvocationError);
        expect(cliErr.message).toContain('unknownNode');
      }
    });

    it('reports port direction mismatch', async () => {
      const workflow = {
        ...validWorkflow,
        connections: [
          {
            id: 'conn-1',
            sourceNodeId: 'start',
            sourcePortName: 'Input',
            targetNodeId: 'http',
            targetPortName: 'Input',
          },
        ],
      };
      const filePath = writeWorkflow('port.json', workflow);

      await expect(
        workflowValidate({ file: filePath, configOptions: options }),
      ).rejects.toThrow(CLIError);

      try {
        await workflowValidate({ file: filePath, configOptions: options });
      } catch (err) {
        const cliErr = err as CLIError;
        expect(cliErr.exitCode).toBe(ExitCode.InvocationError);
        expect(cliErr.message).toContain('Output');
      }
    });

    it('reports missing required parameter', async () => {
      const workflow = {
        ...validWorkflow,
        nodes: [
          manualTriggerNode,
          {
            ...httpRequestNode,
            parameters: { method: 'GET' },
          },
        ],
      };
      const filePath = writeWorkflow('param.json', workflow);

      await expect(
        workflowValidate({ file: filePath, configOptions: options }),
      ).rejects.toThrow(CLIError);

      try {
        await workflowValidate({ file: filePath, configOptions: options });
      } catch (err) {
        const cliErr = err as CLIError;
        expect(cliErr.exitCode).toBe(ExitCode.InvocationError);
        expect(cliErr.message).toContain('url');
      }
    });

    it('reports missing entry node', async () => {
      const workflow = {
        ...validWorkflow,
        nodes: [manualTriggerNode, { ...httpRequestNode, isEntry: false }],
      };
      workflow.nodes[0].isEntry = false;
      const filePath = writeWorkflow('entry.json', workflow);

      await expect(
        workflowValidate({ file: filePath, configOptions: options }),
      ).rejects.toThrow(CLIError);

      try {
        await workflowValidate({ file: filePath, configOptions: options });
      } catch (err) {
        const cliErr = err as CLIError;
        expect(cliErr.exitCode).toBe(ExitCode.InvocationError);
        expect(cliErr.message).toContain('入口');
      }
    });

    it('JSON mode returns structured validation result', async () => {
      const workflow = {
        ...validWorkflow,
        nodes: [
          {
            id: 'bad',
            typeName: 'notImplementedNode',
            name: 'Bad',
            parameters: {},
            ports: [{ name: 'Output', direction: 'Output', type: 'Main' }],
            positionX: 0,
            positionY: 0,
            isEntry: true,
          },
        ],
      };
      const filePath = writeWorkflow('invalid-json.json', workflow);

      setOutputOptions({ json: true, verbose: false });
      const originalExitCode = process.exitCode;
      process.exitCode = 0;
      const spy = vi.spyOn(console, 'log').mockImplementation(() => {});

      await workflowValidate({ file: filePath, configOptions: options });

      expect(spy).toHaveBeenCalled();
      const parsed = JSON.parse(spy.mock.calls[0][0] as string);
      expect(parsed.valid).toBe(false);
      expect(parsed.errors.length).toBeGreaterThan(0);
      expect(Array.isArray(parsed.warnings)).toBe(true);
      expect(process.exitCode).toBe(ExitCode.InvocationError);

      spy.mockRestore();
      process.exitCode = originalExitCode;
      setOutputOptions({ json: false, verbose: false });
    });
  });
});

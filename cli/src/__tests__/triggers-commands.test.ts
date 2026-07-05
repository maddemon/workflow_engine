import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import axios, { type AxiosInstance } from 'axios';
import { mkdtempSync, rmSync } from 'node:fs';
import { join } from 'node:path';
import { tmpdir } from 'node:os';
import {
  triggerCreate,
  triggerDelete,
  triggerGet,
  triggerList,
  triggerUpdate,
} from '../commands/triggers.js';
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

describe('commands/triggers', () => {
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
    tempDir = mkdtempSync(join(tmpdir(), 'flowengine-cli-triggers-test-'));
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
      },
      options,
    );
  });

  afterEach(() => {
    rmSync(tempDir, { recursive: true, force: true });
    vi.restoreAllMocks();
  });

  it('list - returns trigger summaries', async () => {
    mockInstance.get.mockResolvedValue({
      data: [
        {
          id: 'trig-1',
          name: 'Daily Schedule',
          type: 'Schedule',
          workflowDefinitionId: 'wf-1',
          workflowVersion: 1,
          isActive: true,
        },
      ],
    });

    const output = await captureStdout(() => triggerList({ configOptions: options }));

    expect(output).toContain('trig-1: Daily Schedule [Schedule, workflow=wf-1, v1, active=true]');
    expect(mockInstance.get).toHaveBeenCalledWith('/triggers', { params: {} });
  });

  it('list - filters by workflow and project', async () => {
    mockInstance.get.mockResolvedValue({ data: [] });

    await triggerList({
      workflow: 'wf-1',
      projectId: 'project-1',
      configOptions: options,
    });

    expect(mockInstance.get).toHaveBeenCalledWith('/triggers', {
      params: { workflowDefinitionId: 'wf-1', projectId: 'project-1' },
    });
  });

  it('get - returns trigger details', async () => {
    mockInstance.get.mockResolvedValue({
      data: {
        id: 'trig-1',
        name: 'Daily Schedule',
        type: 'Schedule',
        workflowDefinitionId: 'wf-1',
        workflowVersion: 1,
        isActive: true,
        settings: { cronExpression: '0 0 * * *' },
      },
    });

    const output = await captureStdout(() => triggerGet({ id: 'trig-1', configOptions: options }));

    expect(output).toContain('ID: trig-1');
    expect(output).toContain('WorkflowVersion: 1');
    expect(output).toContain('cronExpression');
    expect(mockInstance.get).toHaveBeenCalledWith('/triggers/trig-1');
  });

  it('create - fetches latest workflow version and posts trigger', async () => {
    mockInstance.get.mockResolvedValueOnce({
      data: { id: 'wf-1', name: 'Workflow One', version: 3, isActive: true, createdBy: 'user-1', createdAt: '2025-01-01T00:00:00Z', nodes: [], connections: [] },
    });
    mockInstance.post.mockResolvedValue({
      data: {
        id: 'trig-1',
        name: 'Schedule Trigger',
        type: 'Schedule',
        workflowDefinitionId: 'wf-1',
        workflowVersion: 3,
        isActive: true,
      },
    });

    await triggerCreate({
      workflow: 'wf-1',
      type: 'Schedule',
      name: 'Schedule Trigger',
      configOptions: options,
    });

    expect(mockInstance.get).toHaveBeenCalledWith('/workflows/wf-1');
    expect(mockInstance.post).toHaveBeenCalledWith('/triggers', {
      workflowDefinitionId: 'wf-1',
      workflowVersion: 3,
      type: 'Schedule',
      name: 'Schedule Trigger',
      isActive: true,
    });
  });

  it('create - parses settings JSON', async () => {
    mockInstance.get.mockResolvedValueOnce({
      data: { id: 'wf-1', name: 'Workflow One', version: 1, isActive: true, createdBy: 'user-1', createdAt: '2025-01-01T00:00:00Z', nodes: [], connections: [] },
    });
    mockInstance.post.mockResolvedValue({
      data: {
        id: 'trig-1',
        name: 'Webhook Trigger',
        type: 'Webhook',
        workflowDefinitionId: 'wf-1',
        workflowVersion: 1,
        isActive: true,
        settings: { webhookPath: '/hook' },
      },
    });

    await triggerCreate({
      workflow: 'wf-1',
      type: 'Webhook',
      name: 'Webhook Trigger',
      settings: '{"webhookPath":"/hook"}',
      active: false,
      configOptions: options,
    });

    expect(mockInstance.post.mock.calls[0][1].settings).toEqual({ webhookPath: '/hook' });
    expect(mockInstance.post.mock.calls[0][1].isActive).toBe(false);
  });

  it('create - rejects invalid trigger type with InvocationError', async () => {
    mockInstance.get.mockResolvedValueOnce({
      data: { id: 'wf-1', name: 'Workflow One', version: 1, isActive: true, createdBy: 'user-1', createdAt: '2025-01-01T00:00:00Z', nodes: [], connections: [] },
    });

    await expect(
      triggerCreate({ workflow: 'wf-1', type: 'Invalid', configOptions: options }),
    ).rejects.toThrow(CLIError);

    try {
      await triggerCreate({ workflow: 'wf-1', type: 'Invalid', configOptions: options });
    } catch (err) {
      const cliErr = err as CLIError;
      expect(cliErr.exitCode).toBe(ExitCode.InvocationError);
    }
  });

  it('update - fetches existing trigger and merges changes', async () => {
    mockInstance.get.mockResolvedValueOnce({
      data: {
        id: 'trig-1',
        name: 'Old Name',
        type: 'Schedule',
        workflowDefinitionId: 'wf-1',
        workflowVersion: 1,
        isActive: false,
        settings: { cronExpression: '0 0 * * *' },
      },
    });
    mockInstance.put.mockResolvedValue({
      data: {
        id: 'trig-1',
        name: 'New Name',
        type: 'Schedule',
        workflowDefinitionId: 'wf-1',
        workflowVersion: 1,
        isActive: true,
        settings: { cronExpression: '0 0 * * *' },
      },
    });

    await triggerUpdate({ id: 'trig-1', name: 'New Name', active: 'true', configOptions: options });

    expect(mockInstance.put).toHaveBeenCalledWith('/triggers/trig-1', {
      name: 'New Name',
      isActive: true,
      settings: { cronExpression: '0 0 * * *' },
    });
  });

  it('update - parses new settings', async () => {
    mockInstance.get.mockResolvedValueOnce({
      data: {
        id: 'trig-1',
        name: 'Old Name',
        type: 'Schedule',
        workflowDefinitionId: 'wf-1',
        workflowVersion: 1,
        isActive: false,
      },
    });
    mockInstance.put.mockResolvedValue({
      data: {
        id: 'trig-1',
        name: 'Old Name',
        type: 'Schedule',
        workflowDefinitionId: 'wf-1',
        workflowVersion: 1,
        isActive: false,
        settings: { cronExpression: '0 12 * * *' },
      },
    });

    await triggerUpdate({
      id: 'trig-1',
      settings: '{"cronExpression":"0 12 * * *"}',
      configOptions: options,
    });

    expect(mockInstance.put).toHaveBeenCalledWith('/triggers/trig-1', {
      name: 'Old Name',
      isActive: false,
      settings: { cronExpression: '0 12 * * *' },
    });
  });

  it('delete - calls delete endpoint with confirm', async () => {
    mockInstance.delete.mockResolvedValue({ data: {} });

    const output = await captureStdout(() =>
      triggerDelete({ id: 'trig-1', confirm: true, configOptions: options }),
    );

    expect(output).toContain('已删除触发器：trig-1');
    expect(mockInstance.delete).toHaveBeenCalledWith('/triggers/trig-1');
  });

  it('delete - rejects empty id with InvocationError', async () => {
    await expect(
      triggerDelete({ id: '   ', confirm: true, configOptions: options }),
    ).rejects.toThrow(CLIError);

    try {
      await triggerDelete({ id: '   ', confirm: true, configOptions: options });
    } catch (err) {
      const cliErr = err as CLIError;
      expect(cliErr.exitCode).toBe(ExitCode.InvocationError);
    }
  });
});

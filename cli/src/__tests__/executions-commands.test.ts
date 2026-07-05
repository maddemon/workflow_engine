import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import axios, { type AxiosInstance } from 'axios';
import { mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { tmpdir } from 'node:os';
import {
  execute,
  executionCancel,
  executionGet,
  executionList,
} from '../commands/executions.js';
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

function makeExecution(id: string, status: string) {
  return {
    id,
    workflowDefinitionId: 'wf-1',
    status,
    startedAt: '2025-01-01T00:00:00Z',
    completedAt: status !== 'Running' ? '2025-01-01T00:00:05Z' : undefined,
    nodeRecords: [
      {
        id: 'nr-1',
        nodeDefinitionId: 'nd-1',
        nodeStringId: 'n1',
        runIndex: 0,
        status: status === 'Completed' ? 'Completed' : 'Running',
        startedAt: '2025-01-01T00:00:00Z',
        output: status === 'Completed' ? { ok: true } : undefined,
      },
    ],
  };
}

describe('commands/executions', () => {
  let tempDir: string;
  let options: ConfigOptions;
  let mockInstance: {
    interceptors: {
      request: { use: ReturnType<typeof vi.fn> };
      response: { use: ReturnType<typeof vi.fn> };
    };
    defaults: { timeout?: number };
    get: ReturnType<typeof vi.fn>;
    post: ReturnType<typeof vi.fn>;
  };

  beforeEach(() => {
    tempDir = mkdtempSync(join(tmpdir(), 'flowengine-cli-executions-test-'));
    options = { configDir: tempDir };
    setOutputOptions({ json: false, verbose: false });

    mockInstance = {
      interceptors: {
        request: { use: vi.fn() },
        response: { use: vi.fn() },
      },
      defaults: {},
      get: vi.fn(),
      post: vi.fn(),
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
    vi.useRealTimers();
  });

  it('execute - starts workflow and outputs execution id', async () => {
    mockInstance.post.mockResolvedValue({ data: { id: 'exec-1', status: 'Started' } });

    const output = await captureStdout(() =>
      execute({ workflowId: 'wf-1', configOptions: options }),
    );

    expect(output).toContain('已启动执行：exec-1');
    expect(mockInstance.post).toHaveBeenCalledWith('/workflows/wf-1/execute', {});
  });

  it('execute - sends inputs and idempotency key', async () => {
    mockInstance.post.mockResolvedValue({ data: { id: 'exec-1' } });

    await execute({
      workflowId: 'wf-1',
      input: '{"name":"alice"}',
      idempotencyKey: 'key-1',
      configOptions: options,
    });

    expect(mockInstance.post).toHaveBeenCalledWith('/workflows/wf-1/execute', {
      inputs: { name: 'alice' },
      idempotencyKey: 'key-1',
    });
  });

  it('execute - rejects non-object input', async () => {
    await expect(
      execute({ workflowId: 'wf-1', input: '"string"', configOptions: options }),
    ).rejects.toThrow(CLIError);

    try {
      await execute({ workflowId: 'wf-1', input: '"string"', configOptions: options });
    } catch (err) {
      const cliErr = err as CLIError;
      expect(cliErr.exitCode).toBe(ExitCode.InvocationError);
    }
  });

  it('execute --wait - polls until completed', async () => {
    mockInstance.post.mockResolvedValue({ data: { id: 'exec-1' } });
    mockInstance.get
      .mockResolvedValueOnce({ data: makeExecution('exec-1', 'Running') })
      .mockResolvedValueOnce({ data: makeExecution('exec-1', 'Completed') });

    const output = await captureStdout(() =>
      execute({
        workflowId: 'wf-1',
        wait: true,
        pollInterval: 10,
        configOptions: options,
      }),
    );

    expect(output).toContain('执行完成：exec-1');
    expect(output).toContain('n1: Completed');
    expect(mockInstance.get).toHaveBeenCalledWith('/executions/exec-1');
  });

  it('execute --wait - times out with EXECUTION_TIMEOUT', async () => {
    mockInstance.post.mockResolvedValue({ data: { id: 'exec-1' } });
    mockInstance.get.mockResolvedValue({ data: makeExecution('exec-1', 'Running') });

    let caught: CLIError | undefined;
    try {
      await execute({
        workflowId: 'wf-1',
        wait: true,
        timeout: 1,
        pollInterval: 100,
        configOptions: options,
      });
    } catch (err) {
      caught = err as CLIError;
    }

    expect(caught).toBeInstanceOf(CLIError);
    expect(caught?.code).toBe(ErrorCode.ExecutionTimeout);
    expect(caught?.exitCode).toBe(ExitCode.BusinessFailure);
  });

  it('execute --test - passes with expect file', async () => {
    const expectPath = join(tempDir, 'expect.json');
    writeFileSync(
      expectPath,
      JSON.stringify({ status: 'Completed', nodes: { n1: { status: 'Completed' } } }),
      'utf-8',
    );

    mockInstance.post.mockResolvedValue({ data: { id: 'exec-1' } });
    mockInstance.get.mockResolvedValue({ data: makeExecution('exec-1', 'Completed') });

    const output = await captureStdout(() =>
      execute({
        workflowId: 'wf-1',
        test: true,
        expect: expectPath,
        pollInterval: 10,
        configOptions: options,
      }),
    );

    expect(output).toContain('执行通过');
    expect(output).toContain('n1: Completed');
  });

  it('execute --test - fails with expect file mismatch', async () => {
    const expectPath = join(tempDir, 'expect.json');
    writeFileSync(
      expectPath,
      JSON.stringify({ status: 'Completed', nodes: { n1: { status: 'Failed' } } }),
      'utf-8',
    );

    mockInstance.post.mockResolvedValue({ data: { id: 'exec-1' } });
    mockInstance.get.mockResolvedValue({ data: makeExecution('exec-1', 'Completed') });

    await expect(
      execute({
        workflowId: 'wf-1',
        test: true,
        expect: expectPath,
        pollInterval: 10,
        configOptions: options,
      }),
    ).rejects.toThrow(CLIError);

    try {
      await execute({
        workflowId: 'wf-1',
        test: true,
        expect: expectPath,
        pollInterval: 10,
        configOptions: options,
      });
    } catch (err) {
      const cliErr = err as CLIError;
      expect(cliErr.code).toBe(ErrorCode.AssertionFailed);
      expect(cliErr.exitCode).toBe(ExitCode.BusinessFailure);
    }
  });

  it('execute --test - outputs node summary without expect file', async () => {
    mockInstance.post.mockResolvedValue({ data: { id: 'exec-1' } });
    mockInstance.get.mockResolvedValue({ data: makeExecution('exec-1', 'Completed') });

    const output = await captureStdout(() =>
      execute({
        workflowId: 'wf-1',
        test: true,
        pollInterval: 10,
        configOptions: options,
      }),
    );

    expect(output).toContain('节点摘要');
    expect(output).toContain('n1: Completed');
  });

  it('execution get - outputs details and node records', async () => {
    mockInstance.get.mockResolvedValue({ data: makeExecution('exec-1', 'Completed') });

    const output = await captureStdout(() =>
      executionGet({ id: 'exec-1', configOptions: options }),
    );

    expect(output).toContain('Execution ID: exec-1');
    expect(output).toContain('n1 (nd-1)');
    expect(mockInstance.get).toHaveBeenCalledWith('/executions/exec-1');
  });

  it('execution list - returns sorted and paged summaries', async () => {
    mockInstance.get.mockResolvedValue({
      data: [
        { id: 'exec-1', workflowDefinitionId: 'wf-1', status: 'Completed', startedAt: '2025-01-01T00:00:00Z' },
        { id: 'exec-2', workflowDefinitionId: 'wf-1', status: 'Failed', startedAt: '2025-01-02T00:00:00Z' },
      ],
    });

    const output = await captureStdout(() =>
      executionList({ workflowId: 'wf-1', page: 1, pageSize: 1, configOptions: options }),
    );

    expect(output).toContain('exec-2: Failed');
    expect(output).not.toContain('exec-1: Completed');
    expect(mockInstance.get).toHaveBeenCalledWith('/workflows/wf-1/executions');
  });

  it('execution cancel - posts cancel endpoint', async () => {
    mockInstance.post.mockResolvedValue({ data: {} });

    const output = await captureStdout(() =>
      executionCancel({ id: 'exec-1', configOptions: options }),
    );

    expect(output).toContain('已取消执行：exec-1');
    expect(mockInstance.post).toHaveBeenCalledWith('/executions/exec-1/cancel');
  });

  it('execution cancel - conflict is handled as business failure', async () => {
    mockInstance.post.mockRejectedValue(
      new CLIError(
        '执行已完成或已取消',
        ErrorCode.Conflict,
        ExitCode.BusinessFailure,
      ),
    );

    await expect(
      executionCancel({ id: 'exec-1', configOptions: options }),
    ).rejects.toThrow(CLIError);

    try {
      await executionCancel({ id: 'exec-1', configOptions: options });
    } catch (err) {
      const cliErr = err as CLIError;
      expect(cliErr.code).toBe(ErrorCode.Conflict);
      expect(cliErr.exitCode).toBe(ExitCode.BusinessFailure);
    }
  });
});

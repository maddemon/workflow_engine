import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import axios, { type AxiosInstance } from 'axios';
import { mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { tmpdir } from 'node:os';
import { test } from '../commands/test.js';
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

function makeDryRunResult() {
  return {
    executionId: 'dry-exec-1',
    status: 'DryRunCompleted',
    nodeSummary: { n1: 'DryRunCompleted', n2: 'DryRunCompleted' },
    nodes: {
      n1: { status: 'DryRunCompleted', output: { ok: true } },
      n2: { status: 'DryRunCompleted', output: { value: 42 } },
    },
  };
}

describe('commands/test', () => {
  let tempDir: string;
  let options: ConfigOptions;
  let mockInstance: {
    interceptors: {
      request: { use: ReturnType<typeof vi.fn> };
      response: { use: ReturnType<typeof vi.fn> };
    };
    defaults: { timeout?: number };
    post: ReturnType<typeof vi.fn>;
  };

  beforeEach(() => {
    tempDir = mkdtempSync(join(tmpdir(), 'flowengine-cli-test-command-test-'));
    options = { configDir: tempDir };
    setOutputOptions({ json: false, verbose: false });

    mockInstance = {
      interceptors: {
        request: { use: vi.fn() },
        response: { use: vi.fn() },
      },
      defaults: {},
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
  });

  function writeWorkflow(extra: Record<string, unknown> = {}): string {
    const filePath = join(tempDir, 'workflow.json');
    writeFileSync(
      filePath,
      JSON.stringify({
        nodes: [{ id: 'n1', typeName: 'Start' }],
        connections: [],
        inputs: { name: 'alice' },
        ...extra,
      }),
      'utf-8',
    );
    return filePath;
  }

  it('default output - prints nodeSummary', async () => {
    const filePath = writeWorkflow();
    mockInstance.post.mockResolvedValue({ data: makeDryRunResult() });

    const output = await captureStdout(() =>
      test({ file: filePath, configOptions: options }),
    );

    expect(output).toContain('Dry-run 完成：dry-exec-1');
    expect(output).toContain('n1: DryRunCompleted');
    expect(output).toContain('n2: DryRunCompleted');
    const body = mockInstance.post.mock.calls[0][1] as {
      nodes: unknown[];
      connections: unknown[];
      inputs?: Record<string, unknown>;
    };
    expect(body.nodes).toHaveLength(1);
    expect(body.inputs).toEqual({ name: 'alice' });
  });

  it('credentials - converts map to array and requires HTTPS', async () => {
    setProfile(
      'default',
      {
        baseUrl: 'https://localhost:5001',
        token: 'test-token',
        userId: 'user-1',
        email: 'a@example.com',
      },
      options,
    );

    const filePath = writeWorkflow();
    mockInstance.post.mockResolvedValue({ data: makeDryRunResult() });

    await test({
      file: filePath,
      credentials: JSON.stringify({
        'order-db': { type: 'apiKey', fields: { apiKey: 'secret' } },
      }),
      configOptions: options,
    });

    const body = mockInstance.post.mock.calls[0][1] as {
      credentials?: Array<{ name: string; type: string; fields: Record<string, string> }>;
    };
    expect(body.credentials).toEqual([
      { name: 'order-db', type: 'apiKey', fields: { apiKey: 'secret' } },
    ]);
  });

  it('credentials - rejects HTTP backend', async () => {
    const filePath = writeWorkflow();

    await expect(
      test({
        file: filePath,
        credentials: JSON.stringify({
          'order-db': { type: 'apiKey', fields: { apiKey: 'secret' } },
        }),
        configOptions: options,
      }),
    ).rejects.toThrow(CLIError);

    try {
      await test({
        file: filePath,
        credentials: JSON.stringify({
          'order-db': { type: 'apiKey', fields: { apiKey: 'secret' } },
        }),
        configOptions: options,
      });
    } catch (err) {
      const cliErr = err as CLIError;
      expect(cliErr.exitCode).toBe(ExitCode.InvocationError);
      expect(cliErr.message).toContain('HTTPS');
    }
  });

  it('expect - passes when result matches', async () => {
    const filePath = writeWorkflow();
    const expectPath = join(tempDir, 'expect.json');
    writeFileSync(
      expectPath,
      JSON.stringify({
        status: 'DryRunCompleted',
        nodes: { n1: { status: 'DryRunCompleted', output: { ok: true } } },
      }),
      'utf-8',
    );
    mockInstance.post.mockResolvedValue({ data: makeDryRunResult() });

    const output = await captureStdout(() =>
      test({ file: filePath, expect: expectPath, configOptions: options }),
    );

    expect(output).toContain('测试通过');
  });

  it('expect - fails with path-level diff', async () => {
    const filePath = writeWorkflow();
    const expectPath = join(tempDir, 'expect.json');
    writeFileSync(
      expectPath,
      JSON.stringify({
        status: 'DryRunCompleted',
        nodes: { n2: { status: 'DryRunCompleted', output: { value: 99 } } },
      }),
      'utf-8',
    );
    mockInstance.post.mockResolvedValue({ data: makeDryRunResult() });

    await expect(
      test({ file: filePath, expect: expectPath, configOptions: options }),
    ).rejects.toThrow(CLIError);

    try {
      await test({ file: filePath, expect: expectPath, configOptions: options });
    } catch (err) {
      const cliErr = err as CLIError;
      expect(cliErr.code).toBe(ErrorCode.AssertionFailed);
      expect(cliErr.exitCode).toBe(ExitCode.BusinessFailure);
    }
  });

  it('rejects missing file', async () => {
    await expect(
      test({ file: join(tempDir, 'missing.json'), configOptions: options }),
    ).rejects.toThrow(CLIError);

    try {
      await test({ file: join(tempDir, 'missing.json'), configOptions: options });
    } catch (err) {
      const cliErr = err as CLIError;
      expect(cliErr.exitCode).toBe(ExitCode.InvocationError);
    }
  });

  it('rejects invalid credentials JSON', async () => {
    const filePath = writeWorkflow();

    await expect(
      test({
        file: filePath,
        credentials: 'not-json',
        configOptions: options,
      }),
    ).rejects.toThrow(CLIError);
  });
});

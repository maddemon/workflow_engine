import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import axios, { type AxiosInstance } from 'axios';
import { mkdtempSync, rmSync } from 'node:fs';
import { join } from 'node:path';
import { tmpdir } from 'node:os';
import {
  credentialCreate,
  credentialDelete,
  credentialEnsure,
  credentialGet,
  credentialList,
  credentialUpdate,
} from '../commands/credentials.js';
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

describe('commands/credentials', () => {
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
    tempDir = mkdtempSync(join(tmpdir(), 'flowengine-cli-credentials-test-'));
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

  it('list - returns credential summaries', async () => {
    mockInstance.get.mockResolvedValue({
      data: [
        {
          id: 'cred-1',
          name: 'API Key',
          type: 'ApiKey',
          projectId: 'project-1',
          fields: { key: '***' },
          createdAt: '2025-01-01T00:00:00Z',
        },
        {
          id: 'cred-2',
          name: 'DB Password',
          type: 'Database',
          fields: { password: '***' },
          createdAt: '2025-01-02T00:00:00Z',
        },
      ],
    });

    const output = await captureStdout(() => credentialList({ configOptions: options }));

    expect(output).toContain('cred-1: API Key [ApiKey], Project: project-1');
    expect(output).toContain('cred-2: DB Password [Database]');
    expect(mockInstance.get).toHaveBeenCalledWith('/credentials', { params: undefined });
  });

  it('list - filters by projectId', async () => {
    mockInstance.get.mockResolvedValue({ data: [] });

    await credentialList({ projectId: 'project-1', configOptions: options });

    expect(mockInstance.get).toHaveBeenCalledWith('/credentials', {
      params: { projectId: 'project-1' },
    });
  });

  it('get - returns credential details', async () => {
    mockInstance.get.mockResolvedValue({
      data: {
        id: 'cred-1',
        name: 'API Key',
        type: 'ApiKey',
        projectId: 'project-1',
        fields: { key: '***' },
        createdAt: '2025-01-01T00:00:00Z',
        updatedAt: '2025-01-03T00:00:00Z',
      },
    });

    const output = await captureStdout(() => credentialGet({ id: 'cred-1', configOptions: options }));

    expect(output).toContain('ID: cred-1');
    expect(output).toContain('Name: API Key');
    expect(output).toContain('Type: ApiKey');
    expect(output).toContain('ProjectId: project-1');
    expect(output).toContain('UpdatedAt: 2025-01-03T00:00:00Z');
    expect(mockInstance.get).toHaveBeenCalledWith('/credentials/cred-1');
  });

  it('create - posts credential dto', async () => {
    mockInstance.post.mockResolvedValue({
      data: {
        id: 'cred-1',
        name: 'API Key',
        type: 'ApiKey',
        fields: { key: '***' },
        createdAt: '2025-01-01T00:00:00Z',
      },
    });

    const output = await captureStdout(() =>
      credentialCreate({
        name: 'API Key',
        type: 'ApiKey',
        fields: '{"key":"secret"}',
        configOptions: options,
      }),
    );

    expect(output).toContain('已创建凭据：cred-1');
    expect(mockInstance.post).toHaveBeenCalledWith('/credentials', {
      name: 'API Key',
      type: 'ApiKey',
      fields: { key: 'secret' },
    });
  });

  it('create - includes projectId', async () => {
    mockInstance.post.mockResolvedValue({
      data: { id: 'cred-1', name: 'API Key', type: 'ApiKey', fields: {}, createdAt: '2025-01-01T00:00:00Z' },
    });

    await credentialCreate({
      name: 'API Key',
      type: 'ApiKey',
      fields: '{}',
      projectId: 'project-1',
      configOptions: options,
    });

    expect(mockInstance.post).toHaveBeenCalledWith('/credentials', {
      name: 'API Key',
      type: 'ApiKey',
      fields: {},
      projectId: 'project-1',
    });
  });

  it('ensure - returns created true for new credential', async () => {
    mockInstance.post.mockResolvedValue({
      data: {
        id: 'cred-1',
        name: 'API Key',
        type: 'ApiKey',
        fields: { key: '***' },
        createdAt: '2025-01-01T00:00:00Z',
        created: true,
      },
    });

    setOutputOptions({ json: true, verbose: false });
    const spy = vi.spyOn(console, 'log').mockImplementation(() => {});

    await credentialEnsure({
      name: 'API Key',
      type: 'ApiKey',
      fields: '{"key":"secret"}',
      configOptions: options,
    });

    expect(spy).toHaveBeenCalledTimes(1);
    const parsed = JSON.parse(spy.mock.calls[0][0] as string);
    expect(parsed.created).toBe(true);
    expect(parsed.id).toBe('cred-1');
    spy.mockRestore();
  });

  it('ensure - defaults created to false when missing', async () => {
    mockInstance.post.mockResolvedValue({
      data: {
        id: 'cred-1',
        name: 'API Key',
        type: 'ApiKey',
        fields: { key: '***' },
        createdAt: '2025-01-01T00:00:00Z',
      },
    });

    setOutputOptions({ json: true, verbose: false });
    const spy = vi.spyOn(console, 'log').mockImplementation(() => {});

    await credentialEnsure({
      name: 'API Key',
      type: 'ApiKey',
      fields: '{"key":"secret"}',
      configOptions: options,
    });

    const parsed = JSON.parse(spy.mock.calls[0][0] as string);
    expect(parsed.created).toBe(false);
    spy.mockRestore();
  });

  it('update - puts credential dto', async () => {
    mockInstance.put.mockResolvedValue({
      data: {
        id: 'cred-1',
        name: 'Updated Key',
        type: 'ApiKey',
        fields: { key: '***' },
        createdAt: '2025-01-01T00:00:00Z',
      },
    });

    const output = await captureStdout(() =>
      credentialUpdate({
        id: 'cred-1',
        name: 'Updated Key',
        fields: '{"key":"new-secret"}',
        configOptions: options,
      }),
    );

    expect(output).toContain('已更新凭据：cred-1');
    expect(mockInstance.put).toHaveBeenCalledWith('/credentials/cred-1', {
      name: 'Updated Key',
      fields: { key: 'new-secret' },
    });
  });

  it('delete - calls delete endpoint with confirm', async () => {
    mockInstance.delete.mockResolvedValue({ data: {} });

    const output = await captureStdout(() =>
      credentialDelete({ id: 'cred-1', confirm: true, configOptions: options }),
    );

    expect(output).toContain('已删除凭据：cred-1');
    expect(mockInstance.delete).toHaveBeenCalledWith('/credentials/cred-1');
  });

  it('delete - rejects empty id with InvocationError', async () => {
    await expect(
      credentialDelete({ id: '   ', confirm: true, configOptions: options }),
    ).rejects.toThrow(CLIError);

    try {
      await credentialDelete({ id: '   ', confirm: true, configOptions: options });
    } catch (err) {
      const cliErr = err as CLIError;
      expect(cliErr.code).toBe(ErrorCode.ValidationError);
      expect(cliErr.exitCode).toBe(ExitCode.InvocationError);
    }
  });

  it('create - rejects invalid JSON fields with InvocationError', async () => {
    await expect(
      credentialCreate({
        name: 'API Key',
        type: 'ApiKey',
        fields: 'not-json',
        configOptions: options,
      }),
    ).rejects.toThrow(CLIError);

    try {
      await credentialCreate({
        name: 'API Key',
        type: 'ApiKey',
        fields: 'not-json',
        configOptions: options,
      });
    } catch (err) {
      const cliErr = err as CLIError;
      expect(cliErr.code).toBe(ErrorCode.ValidationError);
      expect(cliErr.exitCode).toBe(ExitCode.InvocationError);
    }
  });

  it('get - encodes id in URL', async () => {
    mockInstance.get.mockResolvedValue({
      data: { id: 'cred/1', name: 'API Key', type: 'ApiKey', fields: {}, createdAt: '2025-01-01T00:00:00Z' },
    });

    await credentialGet({ id: 'cred/1', configOptions: options });

    expect(mockInstance.get).toHaveBeenCalledWith('/credentials/cred%2F1');
  });
});

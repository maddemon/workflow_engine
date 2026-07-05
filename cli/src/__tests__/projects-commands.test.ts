import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import axios, { type AxiosInstance } from 'axios';
import { mkdtempSync, rmSync } from 'node:fs';
import { join } from 'node:path';
import { tmpdir } from 'node:os';
import { projectGet, projectList } from '../commands/projects.js';
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

describe('commands/projects', () => {
  let tempDir: string;
  let options: ConfigOptions;
  let mockInstance: {
    interceptors: {
      request: { use: ReturnType<typeof vi.fn> };
      response: { use: ReturnType<typeof vi.fn> };
    };
    get: ReturnType<typeof vi.fn>;
  };

  beforeEach(() => {
    tempDir = mkdtempSync(join(tmpdir(), 'flowengine-cli-projects-test-'));
    options = { configDir: tempDir };
    setOutputOptions({ json: false, verbose: false });

    mockInstance = {
      interceptors: {
        request: { use: vi.fn() },
        response: { use: vi.fn() },
      },
      get: vi.fn(),
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

  it('list - returns project summaries', async () => {
    mockInstance.get.mockResolvedValue({
      data: [
        {
          id: 'project-1',
          name: 'Project One',
          description: 'First project',
          createdBy: 'user-1',
          createdAt: '2025-01-01T00:00:00Z',
        },
        {
          id: 'project-2',
          name: 'Project Two',
          createdBy: 'user-2',
          createdAt: '2025-01-02T00:00:00Z',
        },
      ],
    });

    const output = await captureStdout(() => projectList({ configOptions: options }));

    expect(output).toContain('project-1: Project One - First project');
    expect(output).toContain('project-2: Project Two');
    expect(mockInstance.get).toHaveBeenCalledWith('/projects');
  });

  it('list - JSON mode outputs parseable array', async () => {
    mockInstance.get.mockResolvedValue({
      data: [
        {
          id: 'project-1',
          name: 'Project One',
          createdBy: 'user-1',
          createdAt: '2025-01-01T00:00:00Z',
        },
      ],
    });

    setOutputOptions({ json: true, verbose: false });
    const spy = vi.spyOn(console, 'log').mockImplementation(() => {});

    await projectList({ configOptions: options });

    expect(spy).toHaveBeenCalledTimes(1);
    const parsed = JSON.parse(spy.mock.calls[0][0] as string);
    expect(Array.isArray(parsed)).toBe(true);
    expect(parsed[0].id).toBe('project-1');
    spy.mockRestore();
  });

  it('get - returns project details', async () => {
    mockInstance.get.mockResolvedValue({
      data: {
        id: 'project-1',
        name: 'Project One',
        description: 'First project',
        createdBy: 'user-1',
        createdAt: '2025-01-01T00:00:00Z',
        updatedAt: '2025-01-03T00:00:00Z',
      },
    });

    const output = await captureStdout(() => projectGet({ id: 'project-1', configOptions: options }));

    expect(output).toContain('ID: project-1');
    expect(output).toContain('Name: Project One');
    expect(output).toContain('Description: First project');
    expect(output).toContain('UpdatedAt: 2025-01-03T00:00:00Z');
    expect(mockInstance.get).toHaveBeenCalledWith('/projects/project-1');
  });

  it('get - rejects empty id', async () => {
    await expect(projectGet({ id: '   ', configOptions: options })).rejects.toThrow(CLIError);

    try {
      await projectGet({ id: '   ', configOptions: options });
    } catch (err) {
      const cliErr = err as CLIError;
      expect(cliErr.code).toBe(ErrorCode.ValidationError);
      expect(cliErr.exitCode).toBe(ExitCode.InvocationError);
    }
  });
});

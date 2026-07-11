import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import axios, { type AxiosInstance } from 'axios';
import { existsSync, mkdtempSync, readFileSync, rmSync } from 'node:fs';
import { join } from 'node:path';
import { tmpdir } from 'node:os';
import { skill } from '../commands/skill.js';
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

describe('commands/skill', () => {
  let tempDir: string;
  let originalCwd: string;
  let options: ConfigOptions;
  let mockInstance: {
    interceptors: {
      request: { use: ReturnType<typeof vi.fn> };
      response: { use: ReturnType<typeof vi.fn> };
    };
    get: ReturnType<typeof vi.fn>;
  };

  beforeEach(() => {
    originalCwd = process.cwd();
    tempDir = mkdtempSync(join(tmpdir(), 'flowengine-cli-skill-test-'));
    process.chdir(tempDir);
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
    process.chdir(originalCwd);
    rmSync(tempDir, { recursive: true, force: true });
    vi.restoreAllMocks();
  });

  it('json format outputs structured skill data', async () => {
    mockInstance.get.mockResolvedValue({
      data: [
        {
          typeName: 'ManualTrigger',
          displayName: '手动触发器',
          category: 'Trigger',
          executionMode: 'OnceForAll',
          defaultIsEntry: true,
          parameters: [],
          ports: [],
        },
      ],
    });

    const spy = vi.spyOn(console, 'log').mockImplementation(() => {});

    await skill({ format: 'json', configOptions: options });

    expect(spy).toHaveBeenCalledTimes(1);
    const parsed = JSON.parse(spy.mock.calls[0][0] as string);
    expect(parsed.format).toBe('json');
    expect(parsed.content.name).toContain('Flow Engine');
    expect(parsed.content.cliCommands).toBeDefined();
    expect(parsed.content.nodeTypes).toBeDefined();
    expect(parsed.content.incomplete).toBeUndefined();
    spy.mockRestore();
  });

  it('includes guide command in cliCommands', async () => {
    mockInstance.get.mockResolvedValue({ data: [] });

    const spy = vi.spyOn(console, 'log').mockImplementation(() => {});
    await skill({ format: 'json', configOptions: options });

    const parsed = JSON.parse(spy.mock.calls[0][0] as string);
    const commands = parsed.content.cliCommands as Array<{ command: string }>;
    expect(commands.some((c) => c.command.startsWith('guide'))).toBe(true);
    spy.mockRestore();
  });

  it('claude format writes SKILL.md by default', async () => {
    mockInstance.get.mockResolvedValue({ data: [] });

    const spy = vi.spyOn(console, 'log').mockImplementation(() => {});
    await skill({ configOptions: options });

    const expectedPath = join(tempDir, '.agents/skills/flow-engine/SKILL.md');
    expect(existsSync(expectedPath)).toBe(true);
    const content = readFileSync(expectedPath, 'utf-8');
    expect(content).toContain('# Flow Engine AI Agent Skill');
    expect(content).toContain('CLI 命令参考');
    expect(spy).toHaveBeenCalledWith(`Skill 已写入：.agents/skills/flow-engine/SKILL.md`);
    spy.mockRestore();
  });

  it('marks incomplete when backend is unavailable', async () => {
    mockInstance.get.mockRejectedValue(new Error('Network Error'));

    const spy = vi.spyOn(console, 'log').mockImplementation(() => {});

    await skill({ format: 'json', configOptions: options });

    const parsed = JSON.parse(spy.mock.calls[0][0] as string);
    expect(parsed.content.incomplete).toBe(true);
    spy.mockRestore();
  });

  it('rejects unsupported format', async () => {
    await expect(skill({ format: 'xml' as 'json', configOptions: options })).rejects.toThrow(CLIError);

    try {
      await skill({ format: 'xml' as 'json', configOptions: options });
    } catch (err) {
      const cliErr = err as CLIError;
      expect(cliErr.code).toBe(ErrorCode.ValidationError);
      expect(cliErr.exitCode).toBe(ExitCode.InvocationError);
    }
  });
});

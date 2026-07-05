import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import axios, { type AxiosInstance } from 'axios';
import { mkdtempSync, rmSync } from 'node:fs';
import { join } from 'node:path';
import { tmpdir } from 'node:os';
import { nodeTypesGet, nodeTypesList } from '../commands/node-types.js';
import { setProfile, type ConfigOptions } from '../config.js';
import { CLIError, ErrorCode, ExitCode } from '../errors.js';
import { setOutputOptions } from '../output.js';
import { ParameterType } from '../types.js';

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

function makeNodeType(overrides?: Partial<{
  typeName: string;
  displayName: string;
  category: string;
  parameters: unknown[];
  ports: unknown[];
}>): unknown {
  return {
    typeName: 'ManualTrigger',
    displayName: '手动触发器',
    category: 'Trigger',
    executionMode: 'OnceForAll',
    defaultIsEntry: true,
    parameters: [],
    ports: [{ name: 'Output', direction: 'Output', type: 'Main', required: true }],
    ...overrides,
  };
}

describe('commands/node-types', () => {
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
    tempDir = mkdtempSync(join(tmpdir(), 'flowengine-cli-node-types-test-'));
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

  it('list - returns summaries without category filter', async () => {
    mockInstance.get.mockResolvedValue({
      data: [
        makeNodeType({ typeName: 'ManualTrigger', displayName: '手动触发器', category: 'Trigger' }),
        makeNodeType({ typeName: 'HttpRequest', displayName: 'HTTP 请求', category: 'Action' }),
      ],
    });

    const output = await captureStdout(() => nodeTypesList({ configOptions: options }));

    expect(output).toContain('[Trigger] ManualTrigger (手动触发器)');
    expect(output).toContain('[Action] HttpRequest (HTTP 请求)');
    expect(mockInstance.get).toHaveBeenCalledWith('/node-types', { params: undefined });
  });

  it('list - filters by category', async () => {
    mockInstance.get.mockResolvedValue({
      data: [makeNodeType({ typeName: 'ManualTrigger', displayName: '手动触发器', category: 'Trigger' })],
    });

    await nodeTypesList({ category: 'Trigger', configOptions: options });

    expect(mockInstance.get).toHaveBeenCalledWith('/node-types', { params: { category: 'Trigger' } });
  });

  it('list - JSON mode outputs parseable array', async () => {
    mockInstance.get.mockResolvedValue({
      data: [makeNodeType({ typeName: 'ManualTrigger', category: 'Trigger' })],
    });

    setOutputOptions({ json: true, verbose: false });
    const spy = vi.spyOn(console, 'log').mockImplementation(() => {});

    await nodeTypesList({ configOptions: options });

    expect(spy).toHaveBeenCalledTimes(1);
    const parsed = JSON.parse(spy.mock.calls[0][0] as string);
    expect(Array.isArray(parsed)).toBe(true);
    expect(parsed[0].typeName).toBe('ManualTrigger');
    spy.mockRestore();
  });

  it('get - finds node type case-insensitively', async () => {
    mockInstance.get.mockResolvedValue({
      data: [
        makeNodeType({ typeName: 'HttpRequest', displayName: 'HTTP 请求', category: 'Action' }),
      ],
    });

    const output = await captureStdout(() =>
      nodeTypesGet({ typeName: 'HTTPREQUEST', configOptions: options }),
    );

    expect(output).toContain('TypeName: HttpRequest');
    expect(output).toContain('DisplayName: HTTP 请求');
    expect(output).toContain('Category: Action');
  });

  it('get - returns NOT_FOUND when type does not exist', async () => {
    mockInstance.get.mockResolvedValue({ data: [] });

    await expect(nodeTypesGet({ typeName: 'Missing', configOptions: options })).rejects.toThrow(
      CLIError,
    );

    try {
      await nodeTypesGet({ typeName: 'Missing', configOptions: options });
    } catch (err) {
      const cliErr = err as CLIError;
      expect(cliErr.code).toBe(ErrorCode.NotFound);
      expect(cliErr.exitCode).toBe(ExitCode.BusinessFailure);
    }
  });

  it('get - highlights Credential parameters', async () => {
    mockInstance.get.mockResolvedValue({
      data: [
        makeNodeType({
          typeName: 'Llm',
          displayName: 'LLM',
          category: 'AI',
          parameters: [
            {
              name: 'apiKeyCredential',
              displayName: 'API Key 凭据',
              type: ParameterType.Credential,
              required: true,
              validationRules: [],
              options: [],
            },
          ],
        }),
      ],
    });

    const output = await captureStdout(() => nodeTypesGet({ typeName: 'llm', configOptions: options }));

    expect(output).toContain('apiKeyCredential');
    expect(output).toContain('需先创建凭据');
  });
});

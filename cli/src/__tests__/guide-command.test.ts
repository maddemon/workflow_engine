import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import axios, { type AxiosInstance } from 'axios';
import { mkdtempSync, rmSync } from 'node:fs';
import { join } from 'node:path';
import { tmpdir } from 'node:os';
import { guide } from '../commands/guide.js';
import { setProfile, type ConfigOptions } from '../config.js';
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

describe('commands/guide', () => {
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
    tempDir = mkdtempSync(join(tmpdir(), 'flowengine-cli-guide-test-'));
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

  it('outputs schema, examples and node type list when backend is available', async () => {
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
        {
          typeName: 'HttpRequest',
          displayName: 'HTTP 请求',
          category: 'Action',
          executionMode: 'OncePerItem',
          defaultIsEntry: false,
          parameters: [],
          ports: [],
        },
      ],
    });

    const output = await captureStdout(() => guide({ configOptions: options }));

    expect(output).toContain('Flow Engine DSL 编写指南');
    expect(output).toContain('顶层结构');
    expect(output).toContain('示例工作流');
    expect(output).toContain('ManualTrigger');
    expect(output).toContain('HttpRequest');
    expect(output).toContain('常见校验错误');
    expect(output).toContain('AI 生成工作流');
    expect(output).toContain('表达式变量参考');
    expect(output).toContain('$node[\'GetUser\']');
    expect(output).toContain('$items(\'GetUser\')');
    expect(output).toContain('$credentials.db.connectionString');
    expect(output).toContain('表达式语法说明');
    expect(output).toContain('Script 类型');
    expect(output).toContain('纯字符串简写');
    expect(output).toContain('SetNode 表达式字段映射');
    expect(output).toContain('钉钉员工同步到数据库');
  });

  it('JSON mode outputs structured guide', async () => {
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

    setOutputOptions({ json: true, verbose: false });
    const spy = vi.spyOn(console, 'log').mockImplementation(() => {});

    await guide({ configOptions: options });

    expect(spy).toHaveBeenCalledTimes(1);
    const parsed = JSON.parse(spy.mock.calls[0][0] as string);
    expect(parsed.schema).toBeDefined();
    expect(parsed.examples).toBeDefined();
    expect(parsed.commonErrors).toBeDefined();
    expect(parsed.nodeTypes).toBeDefined();
    expect(parsed.aiGeneration).toBeDefined();
    expect(parsed.variableReference).toBeDefined();
    expect(Array.isArray(parsed.variableReference.variables)).toBe(true);
    expect(parsed.variableReference.variables[0].example).toBeDefined();
    expect(parsed.expressionSyntax).toBeDefined();
    expect(parsed.expressionSyntax.scriptType).toContain('source');
    expect(parsed.incomplete).toBeUndefined();
    spy.mockRestore();
  });

  it('outputs incomplete template when backend is unavailable', async () => {
    mockInstance.get.mockRejectedValue(new Error('Network Error'));

    const output = await captureStdout(() => guide({ configOptions: options }));

    expect(output).toContain('Flow Engine DSL 编写指南');
    expect(output).toContain('未连接后端');
    expect(output).toContain('节点类型清单不可用');
  });

  it('JSON mode marks incomplete when backend is unavailable', async () => {
    mockInstance.get.mockRejectedValue(new Error('Network Error'));

    setOutputOptions({ json: true, verbose: false });
    const spy = vi.spyOn(console, 'log').mockImplementation(() => {});

    await guide({ configOptions: options });

    const parsed = JSON.parse(spy.mock.calls[0][0] as string);
    expect(parsed.incomplete).toBe(true);
    expect(parsed.schema).toBeDefined();
    expect(parsed.examples).toBeDefined();
    spy.mockRestore();
  });

  it('shows offline notice and known capability gaps when backend is unavailable', async () => {
    mockInstance.get.mockRejectedValue(new Error('Network Error'));

    const output = await captureStdout(() => guide({ configOptions: options }));

    expect(output).toContain('未连接后端');
    expect(output).toContain('节点类型清单不可用');
    expect(output).toContain('已知能力缺口');
    expect(output).toContain('manualTrigger');
  });
});

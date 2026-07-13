import { describe, it, expect, vi, beforeEach } from 'vitest';
import { createProxy } from '../src/proxy.js';
import type { HttpClient, HttpResponse } from '../src/http-client.js';

function createMockHttpClient(responses: HttpResponse[]): HttpClient {
  let callIndex = 0;
  return {
    post: vi.fn(async (_path: string, _request: { body: string; headers?: Record<string, string> }): Promise<HttpResponse> => {
      const response = responses[callIndex] ?? { status: 500, body: 'no mock response', headers: {} };
      callIndex++;
      return response;
    }),
  };
}

function createMockStreams() {
  const output: string[] = [];
  const errors: string[] = [];

  return {
    streams: {
      stdin: { readable: true } as unknown as NodeJS.ReadableStream,
      stdout: {
        write: vi.fn((data: string) => { output.push(data); return true; }),
      } as unknown as NodeJS.WritableStream,
      stderr: {
        write: vi.fn((data: string) => { errors.push(data); return true; }),
      } as unknown as NodeJS.WritableStream,
    },
    output,
    errors,
  };
}

describe('proxy', () => {
  describe('environment variable validation (via index.ts exit)', () => {
    it('should exit with error when FLOWENGINE_URL is missing', async () => {
      const originalEnv = process.env;
      process.env = { ...originalEnv, FLOWENGINE_URL: '', FLOWENGINE_API_KEY: 'key' };

      const { execFile } = await import('node:child_process');
      const { promisify } = await import('node:util');
      const execAsync = promisify(execFile);

      try {
        const { stderr } = await execAsync('node', ['dist/index.js'], {
          cwd: import.meta.dirname + '/../',
          env: { ...process.env, FLOWENGINE_URL: '', FLOWENGINE_API_KEY: 'key' },
          timeout: 5000,
        });
        // If we get here, it should have written to stderr
        expect(stderr).toContain('FLOWENGINE_URL');
      } catch (err: unknown) {
        const error = err as { stderr?: string };
        expect(error.stderr).toContain('FLOWENGINE_URL');
      }

      process.env = originalEnv;
    });

    it('should exit with error when FLOWENGINE_API_KEY is missing', async () => {
      const { execFile } = await import('node:child_process');
      const { promisify } = await import('node:util');
      const execAsync = promisify(execFile);

      try {
        const { stderr } = await execAsync('node', ['dist/index.js'], {
          cwd: import.meta.dirname + '/../',
          env: { ...process.env, FLOWENGINE_URL: 'http://localhost:8001', FLOWENGINE_API_KEY: '' },
          timeout: 5000,
        });
        expect(stderr).toContain('FLOWENGINE_API_KEY');
      } catch (err: unknown) {
        const error = err as { stderr?: string };
        expect(error.stderr).toContain('FLOWENGINE_API_KEY');
      }
    });
  });

  describe('message forwarding', () => {
    it('should forward MCP messages to /mcp', async () => {
      const mockResponse: HttpResponse = {
        status: 200,
        body: '{"jsonrpc":"2.0","result":{},"id":1}',
        headers: {},
      };
      const httpClient = createMockHttpClient([mockResponse]);
      const { streams, output } = createMockStreams();

      const proxy = createProxy({ httpClient, streams });
      await proxy.handleMessage('{"jsonrpc":"2.0","method":"initialize","id":1}');

      expect(httpClient.post).toHaveBeenCalledWith('/mcp', {
        body: '{"jsonrpc":"2.0","method":"initialize","id":1}',
        headers: {},
      });
      expect(output).toContain('{"jsonrpc":"2.0","result":{},"id":1}\n');
    });

    it('should write response to stdout', async () => {
      const responseBody = '{"jsonrpc":"2.0","result":{"capabilities":{}},"id":1}';
      const mockResponse: HttpResponse = {
        status: 200,
        body: responseBody,
        headers: {},
      };
      const httpClient = createMockHttpClient([mockResponse]);
      const { streams, output } = createMockStreams();

      const proxy = createProxy({ httpClient, streams });
      await proxy.handleMessage('{"jsonrpc":"2.0","method":"initialize","id":1}');

      expect(output).toContain(`${responseBody}\n`);
    });

    it('should persist Mcp-Session-Id from response to subsequent requests', async () => {
      const firstResponse: HttpResponse = {
        status: 200,
        body: '{"jsonrpc":"2.0","result":{},"id":1}',
        headers: { 'mcp-session-id': 'session-abc-123' },
      };
      const secondResponse: HttpResponse = {
        status: 200,
        body: '{"jsonrpc":"2.0","result":{},"id":2}',
        headers: {},
      };
      const httpClient = createMockHttpClient([firstResponse, secondResponse]);
      const { streams } = createMockStreams();

      const proxy = createProxy({ httpClient, streams });

      // First message — should NOT have session header
      await proxy.handleMessage('{"jsonrpc":"2.0","method":"initialize","id":1}');
      expect(httpClient.post).toHaveBeenLastCalledWith('/mcp', {
        body: '{"jsonrpc":"2.0","method":"initialize","id":1}',
        headers: {},
      });

      // Session ID should now be stored
      expect(proxy.getSessionId()).toBe('session-abc-123');

      // Second message — should carry session header
      await proxy.handleMessage('{"jsonrpc":"2.0","method":"tools/list","id":2}');
      expect(httpClient.post).toHaveBeenLastCalledWith('/mcp', {
        body: '{"jsonrpc":"2.0","method":"tools/list","id":2}',
        headers: { 'Mcp-Session-Id': 'session-abc-123' },
      });
    });

    it('should write error to stderr when HTTP fails but not throw', async () => {
      const httpClient = createMockHttpClient([]);
      (httpClient.post as ReturnType<typeof vi.fn>).mockRejectedValueOnce(new Error('Connection refused'));

      const { streams, errors } = createMockStreams();
      const proxy = createProxy({ httpClient, streams });

      // Should NOT throw
      await expect(proxy.handleMessage('{"jsonrpc":"2.0","method":"test","id":1}')).resolves.toBeUndefined();
      expect(errors.some(e => e.includes('Connection refused'))).toBe(true);
    });

    it('should write HTTP error status to stderr but still write response to stdout', async () => {
      const errorResponse: HttpResponse = {
        status: 401,
        body: '{"error":"Unauthorized"}',
        headers: {},
      };
      const httpClient = createMockHttpClient([errorResponse]);
      const { streams, output, errors } = createMockStreams();

      const proxy = createProxy({ httpClient, streams });
      await proxy.handleMessage('{"jsonrpc":"2.0","method":"test","id":1}');

      expect(errors.some(e => e.includes('HTTP 401'))).toBe(true);
      expect(output).toContain('{"error":"Unauthorized"}\n');
    });

    it('should skip empty lines', async () => {
      const httpClient = createMockHttpClient([]);
      const { streams } = createMockStreams();

      const proxy = createProxy({ httpClient, streams });
      await proxy.handleMessage('   ');

      expect(httpClient.post).not.toHaveBeenCalled();
    });
  });
});

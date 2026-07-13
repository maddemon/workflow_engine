/**
 * stdio→HTTP proxy logic for the MCP shim.
 * Dumb proxy: reads JSON-RPC from stdin, forwards to /mcp, writes responses to stdout.
 * Handles Mcp-Session-Id header persistence.
 */

import type { HttpClient } from './http-client.js';

export interface ProxyStreams {
  stdin: NodeJS.ReadableStream;
  stdout: NodeJS.WritableStream;
  stderr: NodeJS.WritableStream;
}

export interface ProxyDeps {
  httpClient: HttpClient;
  streams: ProxyStreams;
}

export function createProxy(deps: ProxyDeps) {
  const { httpClient, streams } = deps;
  let sessionId: string | undefined;

  return {
    async handleMessage(line: string): Promise<void> {
      const trimmed = line.trim();
      if (!trimmed) return;

      const headers: Record<string, string> = {};
      if (sessionId) {
        headers['Mcp-Session-Id'] = sessionId;
      }

      try {
        const response = await httpClient.post('/mcp', {
          body: trimmed,
          headers,
        });

        const responseSessionId = response.headers['mcp-session-id'];
        if (responseSessionId) {
          sessionId = responseSessionId;
        }

        if (response.status >= 400) {
          streams.stderr.write(`HTTP ${response.status}: ${response.body}\n`);
        }

        streams.stdout.write(`${response.body}\n`);
      } catch (err) {
        const message = err instanceof Error ? err.message : String(err);
        streams.stderr.write(`Proxy error: ${message}\n`);
      }
    },

    getSessionId(): string | undefined {
      return sessionId;
    },
  };
}

export type Proxy = ReturnType<typeof createProxy>;

import { defaultConfig, type DogfoodingConfig } from '../config.default.js';
import type { McpToolSpec } from './types.js';

export class McpClient {
  private sessionId: string | null = null;
  private tools: McpToolSpec[] = [];
  private requestId = 0;
  private config: DogfoodingConfig['flowEngine'];

  constructor(config?: DogfoodingConfig['flowEngine']) {
    this.config = config ?? defaultConfig.flowEngine;
  }

  async initialize(): Promise<McpToolSpec[]> {
    // MCP Streamable HTTP requires an initialize handshake first
    await this.sendRequest('initialize', {
      protocolVersion: '2025-03-26',
      capabilities: {},
      clientInfo: { name: 'dogfooding', version: '1.0.0' },
    });
    // then list tools
    const result = await this.sendRequest('tools/list', {});
    const list = result as { tools?: McpToolSpec[] } | undefined;
    this.tools = list?.tools ?? [];
    return this.tools;
  }

  async callTool<T>(name: string, args: Record<string, unknown>): Promise<T> {
    const result = await this.sendRequest('tools/call', { name, arguments: args });
    return result as T;
  }

  listTools(): McpToolSpec[] {
    return this.tools;
  }

  async close(): Promise<void> {
    this.sessionId = null;
  }

  private async sendRequest(method: string, params: unknown): Promise<unknown> {
    const id = ++this.requestId;
    const headers: Record<string, string> = {
      'Content-Type': 'application/json',
      'Authorization': `Bearer ${this.config.apiKey}`,
    };
    if (this.sessionId) {
      headers['Mcp-Session-Id'] = this.sessionId;
    }

    const response = await fetch(`${this.config.url}/mcp`, {
      method: 'POST',
      headers,
      body: JSON.stringify({ jsonrpc: '2.0', id, method, params }),
    });

    if (!response.ok) {
      throw new Error(`MCP HTTP ${response.status}: ${await response.text()}`);
    }

    const sessionHeader = response.headers.get('Mcp-Session-Id');
    if (sessionHeader && !this.sessionId) {
      this.sessionId = sessionHeader;
    }

    // MCP Streamable HTTP may return SSE (text/event-stream) or direct JSON
    const text = await response.text();
    const contentType = response.headers.get('content-type') ?? '';

    let parsed: { jsonrpc: string; id: number; result?: unknown; error?: { code: number; message: string } };
    if (contentType.includes('text/event-stream') || text.startsWith('data:') || text.startsWith('event:')) {
      // SSE format: extract JSON from data: lines
      const jsonLine = text.split('\n')
        .find(line => line.startsWith('data:'))
        ?.slice(5)
        ?.trim();
      if (!jsonLine) {
        throw new Error(`MCP SSE response with no data line: ${text.slice(0, 200)}`);
      }
      parsed = JSON.parse(jsonLine);
    } else {
      parsed = JSON.parse(text);
    }

    if (parsed.error) {
      throw new Error(`MCP Error ${parsed.error.code}: ${parsed.error.message}`);
    }

    // Unwrap MCP content format: { content: [{ type: 'text', text: '{...}' }] }
    const result = parsed.result as Record<string, unknown> | undefined;
    if (result?.content && Array.isArray(result.content) && result.content.length > 0) {
      const first = result.content[0] as Record<string, unknown> | undefined;
      if (first?.type === 'text' && typeof first.text === 'string') {
        try {
          return JSON.parse(first.text);
        } catch {
          return first.text;
        }
      }
    }

    return result;
  }
}

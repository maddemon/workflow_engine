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
    const tools = await this.sendRequest('tools/list', {});
    this.tools = (tools as McpToolSpec[]) ?? [];
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

    const result = await response.json() as { jsonrpc: string; id: number; result?: unknown; error?: { code: number; message: string } };

    if (result.error) {
      throw new Error(`MCP Error ${result.error.code}: ${result.error.message}`);
    }

    return result.result;
  }
}

import { describe, it, expect, vi, beforeEach } from 'vitest';
import { McpClient } from '../src/mcp-client.js';

describe('McpClient', () => {
  let client: McpClient;
  const mockFetch = vi.fn();

  beforeEach(() => {
    vi.stubGlobal('fetch', mockFetch);
    client = new McpClient({ url: 'http://test:8001', apiKey: 'test-key' });
  });

  it('initialize - returns tools and stores session', async () => {
    mockFetch.mockResolvedValueOnce({
      ok: true,
      headers: new Map([['Mcp-Session-Id', 'sess-123']]),
      json: async () => ({
        jsonrpc: '2.0',
        id: 1,
        result: [{ name: 'list_node_catalog', description: 'List nodes' }],
      }),
    });

    const tools = await client.initialize();
    expect(tools).toHaveLength(1);
    expect(tools[0].name).toBe('list_node_catalog');

    // 验证 session ID 被存储
    const callArgs = mockFetch.mock.calls[0];
    expect(callArgs[1].headers['Mcp-Session-Id']).toBeUndefined(); // 首次无 session

    // 第二次调用应带 session
    mockFetch.mockResolvedValueOnce({
      ok: true,
      headers: new Map(),
      json: async () => ({
        jsonrpc: '2.0',
        id: 2,
        result: { draftId: 'wf-1' },
      }),
    });

    await client.callTool('assemble_workflow', { name: 'test' });
    const callArgs2 = mockFetch.mock.calls[1];
    expect(callArgs2[1].headers['Mcp-Session-Id']).toBe('sess-123');
  });

  it('callTool - returns result', async () => {
    mockFetch.mockResolvedValueOnce({
      ok: true,
      headers: new Map(),
      json: async () => ({
        jsonrpc: '2.0',
        id: 1,
        result: { draftId: 'wf-1', workflow: { name: 'test' } },
      }),
    });

    const result = await client.callTool('assemble_workflow', { name: 'test', nodes: [] });
    expect(result).toEqual({ draftId: 'wf-1', workflow: { name: 'test' } });
  });

  it('callTool - throws on MCP error', async () => {
    mockFetch.mockResolvedValueOnce({
      ok: true,
      headers: new Map(),
      json: async () => ({
        jsonrpc: '2.0',
        id: 1,
        error: { code: -32602, message: 'Invalid params' },
      }),
    });

    await expect(client.callTool('assemble_workflow', {})).rejects.toThrow('MCP Error -32602');
  });

  it('callTool - throws on HTTP error', async () => {
    mockFetch.mockResolvedValueOnce({
      ok: false,
      status: 401,
      text: async () => 'Unauthorized',
    });

    await expect(client.callTool('assemble_workflow', {})).rejects.toThrow('MCP HTTP 401');
  });
});

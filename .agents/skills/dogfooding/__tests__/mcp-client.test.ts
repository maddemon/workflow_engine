import { describe, it, expect, vi, beforeEach } from 'vitest';
import { McpClient } from '../src/mcp-client.js';

function sseResponse(data: unknown, headers?: Record<string, string>) {
  return {
    ok: true,
    headers: new Map(Object.entries(headers ?? {})),
    text: async () => `data: ${JSON.stringify(data)}\n\n`,
  };
}

describe('McpClient', () => {
  let client: McpClient;
  const mockFetch = vi.fn();

  beforeEach(() => {
    vi.stubGlobal('fetch', mockFetch);
    client = new McpClient({ url: 'http://test:8001', apiKey: 'test-key' });
  });

  it('initialize - sends initialize handshake then tools/list, stores session', async () => {
    // First call: initialize handshake -> returns session
    mockFetch.mockResolvedValueOnce(sseResponse(
      { jsonrpc: '2.0', id: 1, result: { protocolVersion: '2025-03-26', capabilities: {}, serverInfo: { name: 'test', version: '1.0' } } },
      { 'Mcp-Session-Id': 'sess-123' },
    ));
    // Second call: tools/list -> returns tool list
    mockFetch.mockResolvedValueOnce(sseResponse(
      { jsonrpc: '2.0', id: 2, result: { tools: [{ name: 'list_node_catalog', description: 'List nodes' }] } },
    ));

    const tools = await client.initialize();
    expect(tools).toHaveLength(1);
    expect(tools[0].name).toBe('list_node_catalog');

    // Verify first call is initialize, no session
    const call1 = mockFetch.mock.calls[0];
    expect(call1[1].headers['Mcp-Session-Id']).toBeUndefined();
    const body1 = JSON.parse(call1[1].body);
    expect(body1.method).toBe('initialize');

    // Verify second call has session
    const call2 = mockFetch.mock.calls[1];
    expect(call2[1].headers['Mcp-Session-Id']).toBe('sess-123');
    const body2 = JSON.parse(call2[1].body);
    expect(body2.method).toBe('tools/list');

    // Subsequent callTool uses session
    mockFetch.mockResolvedValueOnce(sseResponse(
      { jsonrpc: '2.0', id: 3, result: { draftId: 'wf-1' } },
    ));
    await client.callTool('assemble_workflow', { name: 'test' });
    const call3 = mockFetch.mock.calls[2];
    expect(call3[1].headers['Mcp-Session-Id']).toBe('sess-123');
  });

  it('callTool - returns result', async () => {
    mockFetch.mockResolvedValueOnce(sseResponse(
      { jsonrpc: '2.0', id: 1, result: { draftId: 'wf-1', workflow: { name: 'test' } } },
    ));

    const result = await client.callTool('assemble_workflow', { name: 'test', nodes: [] });
    expect(result).toEqual({ draftId: 'wf-1', workflow: { name: 'test' } });
  });

  it('callTool - throws on MCP error', async () => {
    mockFetch.mockResolvedValueOnce({
      ok: true,
      headers: new Map(),
      text: async () => `data: ${JSON.stringify({ jsonrpc: '2.0', id: 1, error: { code: -32602, message: 'Invalid params' } })}\n\n`,
    });

    await expect(client.callTool('assemble_workflow', {})).rejects.toThrow('MCP Error -32602');
  });

  it('callTool - throws on HTTP error', async () => {
    mockFetch.mockResolvedValueOnce({
      ok: false,
      status: 401,
      headers: new Map(),
      text: async () => 'Unauthorized',
    });

    await expect(client.callTool('assemble_workflow', {})).rejects.toThrow('MCP HTTP 401');
  });

  it('callTool - handles direct JSON (non-SSE) response', async () => {
    // Some MCP servers may return direct JSON without SSE wrapping
    mockFetch.mockResolvedValueOnce({
      ok: true,
      headers: new Map([['content-type', 'application/json']]),
      text: async () => JSON.stringify({ jsonrpc: '2.0', id: 1, result: { status: 'ok' } }),
    });

    const result = await client.callTool('test_tool', {});
    expect(result).toEqual({ status: 'ok' });
  });
});

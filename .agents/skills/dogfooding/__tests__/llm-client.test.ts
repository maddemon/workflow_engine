import { describe, it, expect, vi, beforeEach } from 'vitest';
import { LlmClient } from '../src/llm-client.js';

describe('LlmClient', () => {
  const mockFetch = vi.fn();

  beforeEach(() => {
    vi.clearAllMocks();
    vi.stubGlobal('fetch', mockFetch);
  });

  it('generate - returns LLM response text', async () => {
    mockFetch.mockResolvedValueOnce({
      ok: true,
      json: async () => ({
        choices: [{ message: { content: 'Hello, world!' } }],
      }),
    });

    const client = new LlmClient({ apiKey: 'key', baseUrl: 'https://api.openai.com/v1', model: 'gpt-4o' });
    const result = await client.generate('Say hello');
    expect(result).toBe('Hello, world!');
  });

  it('generate - sends correct request body', async () => {
    mockFetch.mockResolvedValueOnce({
      ok: true,
      json: async () => ({ choices: [{ message: { content: 'ok' } }] }),
    });

    const client = new LlmClient({ apiKey: 'key', baseUrl: 'https://api.openai.com/v1', model: 'gpt-4o' });
    await client.generate('Hello', { system: 'Be polite' });

    const callBody = JSON.parse(mockFetch.mock.calls[0][1].body);
    expect(callBody.model).toBe('gpt-4o');
    expect(callBody.messages[0].role).toBe('system');
    expect(callBody.messages[0].content).toBe('Be polite');
    expect(callBody.messages[1].content).toBe('Hello');
  });

  it('generateJson - parses structured output', async () => {
    mockFetch.mockResolvedValueOnce({
      ok: true,
      json: async () => ({
        choices: [{ message: { content: '{"items": [1, 2, 3]}' } }],
      }),
    });

    const client = new LlmClient({ apiKey: 'key', baseUrl: 'https://api.openai.com/v1', model: 'gpt-4o' });
    const result = await client.generateJson<{ items: number[] }>('Give me numbers');
    expect(result.items).toEqual([1, 2, 3]);
  });

  it('generate - throws on API error', async () => {
    mockFetch.mockResolvedValueOnce({
      ok: false,
      status: 429,
      json: async () => ({ error: { message: 'Rate limited' } }),
    });

    const client = new LlmClient({ apiKey: 'key', baseUrl: 'https://api.openai.com/v1', model: 'gpt-4o' });
    await expect(client.generate('test')).rejects.toThrow(/429/);
  });
});

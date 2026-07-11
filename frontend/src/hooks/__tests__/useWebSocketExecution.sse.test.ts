import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { renderHook, act } from '@testing-library/react';
import { useWebSocketExecution } from '../useWebSocketExecution';
import { useWorkflowStore } from '../../stores/workflowStore';

class MockWebSocket {
  static CONNECTING = 0;
  static OPEN = 1;
  static CLOSING = 2;
  static CLOSED = 3;
  readyState = MockWebSocket.CONNECTING;
  onopen: (() => void) | null = null;
  onclose: (() => void) | null = null;
  onerror: (() => void) | null = null;
  onmessage: ((event: { data: string }) => void) | null = null;
  url: string;

  constructor(url: string) {
    this.url = url;
    mockWebSockets.push(this);
  }

  close() {
    if (this.readyState === MockWebSocket.CLOSED) {
      return;
    }
    this.readyState = MockWebSocket.CLOSED;
    this.onclose?.();
  }

  send(_data: string) {
    void _data;
  }
}

class MockEventSource {
  url: string;
  onmessage: ((event: MessageEvent) => void) | null = null;
  onerror: (() => void) | null = null;

  constructor(url: string) {
    this.url = url;
    mockEventSources.push(this);
  }

  close() {
    mockEventSources = mockEventSources.filter((es) => es !== this);
  }
}

let mockWebSockets: MockWebSocket[] = [];
let mockEventSources: MockEventSource[] = [];

const localStorageMock = (() => {
  let store: Record<string, string> = {};
  return {
    getItem: (key: string) => store[key] ?? null,
    setItem: (key: string, value: string) => {
      store[key] = value;
    },
    removeItem: (key: string) => {
      delete store[key];
    },
    clear: () => {
      store = {};
    },
  };
})();

Object.defineProperty(window, 'localStorage', {
  value: localStorageMock,
  writable: true,
});

describe('useWebSocketExecution SSE fallback', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    vi.clearAllMocks();
    localStorageMock.clear();
    mockWebSockets = [];
    mockEventSources = [];
    (globalThis as unknown as typeof globalThis & { WebSocket: typeof WebSocket }).WebSocket = MockWebSocket as unknown as typeof WebSocket;
    (globalThis as unknown as typeof globalThis & { EventSource: typeof EventSource }).EventSource = MockEventSource as unknown as typeof EventSource;
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('does NOT append access_token to SSE URL (uses HttpOnly cookie auth)', () => {
    localStorage.setItem('auth_token', 'test-jwt-token');

    const { result } = renderHook(() => useWebSocketExecution());

    act(() => {
      result.current.connect();
      result.current.subscribe('exec-jwt');
    });

    for (let i = 0; i < 6; i++) {
      act(() => {
        const current = mockWebSockets[mockWebSockets.length - 1];
        current?.close();
      });
      if (i < 5) {
        act(() => {
          vi.advanceTimersByTime(2000 * Math.pow(2, i));
        });
      }
    }

    expect(mockEventSources.length).toBe(1);
    // 安全加固：SSE 不再通过 query 暴露令牌，改由同源 HttpOnly Cookie（fe_auth）携带，避免 URL 泄露
    expect(mockEventSources[0].url).toBe('/api/v1/executions/exec-jwt/stream');
    expect(result.current.status).toBe('connected');

    localStorage.removeItem('auth_token');
  });

  it('creates an EventSource with the expected URL after WebSocket reconnects are exhausted', () => {
    const { result } = renderHook(() => useWebSocketExecution());

    act(() => {
      result.current.connect();
      result.current.subscribe('exec-123');
    });

    for (let i = 0; i < 6; i++) {
      act(() => {
        const current = mockWebSockets[mockWebSockets.length - 1];
        current?.close();
      });
      if (i < 5) {
        act(() => {
          vi.advanceTimersByTime(2000 * Math.pow(2, i));
        });
      }
    }

    expect(mockEventSources.length).toBe(1);
    expect(mockEventSources[0].url).toBe('/api/v1/executions/exec-123/stream');
    expect(result.current.status).toBe('connected');
  });

  it('parses SSE messages and updates the workflow store', () => {
    const { result } = renderHook(() => useWebSocketExecution());

    act(() => {
      result.current.connect();
      result.current.subscribe('exec-456');
    });

    for (let i = 0; i < 6; i++) {
      act(() => {
        const current = mockWebSockets[mockWebSockets.length - 1];
        current?.close();
      });
      if (i < 5) {
        act(() => {
          vi.advanceTimersByTime(2000 * Math.pow(2, i));
        });
      }
    }

    act(() => {
      mockEventSources[0].onmessage?.(
        { data: JSON.stringify({
          type: 'node_executed',
          executionId: 'exec-456',
          timestamp: '2026-07-05T00:00:00.000Z',
          sequence: 7,
          payload: {
            nodeDefinitionId: 'node-a',
            runIndex: 0,
            result: { success: true, itemCount: 3 },
          },
        }) } as MessageEvent,
      );
    });

    expect(result.current.lastSequence).toBe(7);
    const record = useWorkflowStore.getState().nodeExecutionRecords['node-a'];
    expect(record).toBeDefined();
    expect(record.status).toBe('Completed');
  });

  it('sets status to error when the EventSource reports an error', () => {
    const { result } = renderHook(() => useWebSocketExecution());

    act(() => {
      result.current.connect();
      result.current.subscribe('exec-789');
    });

    for (let i = 0; i < 6; i++) {
      act(() => {
        const current = mockWebSockets[mockWebSockets.length - 1];
        current?.close();
      });
      if (i < 5) {
        act(() => {
          vi.advanceTimersByTime(2000 * Math.pow(2, i));
        });
      }
    }

    act(() => {
      mockEventSources[0].onerror?.();
    });

    expect(result.current.status).toBe('error');
    expect(mockEventSources.length).toBe(0);
  });

  it('closes the active EventSource on disconnect', () => {
    const { result } = renderHook(() => useWebSocketExecution());

    act(() => {
      result.current.connect();
      result.current.subscribe('exec-000');
    });

    for (let i = 0; i < 6; i++) {
      act(() => {
        const current = mockWebSockets[mockWebSockets.length - 1];
        current?.close();
      });
      if (i < 5) {
        act(() => {
          vi.advanceTimersByTime(2000 * Math.pow(2, i));
        });
      }
    }

    act(() => {
      result.current.disconnect();
    });

    expect(mockEventSources.length).toBe(0);
  });
});

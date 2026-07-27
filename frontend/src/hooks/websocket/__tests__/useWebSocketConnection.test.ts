import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { renderHook, act } from '@testing-library/react';
import type { MutableRefObject } from 'react';
import { useWebSocketConnection } from '../useWebSocketConnection.ts';

/**
 * 由于 jsdom 不提供 WebSocket 实现，这里用一个最小可用的假类替换全局 WebSocket。
 * 通过追踪构造实例数量，可以判定「是否发起了重连（即新建了第二个 WebSocket）」。
 */
class MockWebSocket {
  private static instances: MockWebSocket[] = [];
  static readonly CONNECTING = 0;
  static readonly OPEN = 1;
  static readonly CLOSING = 2;
  static readonly CLOSED = 3;

  url: string;
  readyState = MockWebSocket.CONNECTING;
  onopen: ((ev?: Event) => void) | null = null;
  onclose: ((ev?: CloseEvent) => void) | null = null;
  onmessage: ((ev: MessageEvent) => void) | null = null;
  onerror: ((ev?: Event) => void) | null = null;
  close = vi.fn(() => {
    this.readyState = MockWebSocket.CLOSED;
  });
  send = vi.fn();

  constructor(url: string) {
    this.url = url;
    MockWebSocket.instances.push(this);
  }

  static get instanceCount(): number {
    return MockWebSocket.instances.length;
  }

  static reset(): void {
    MockWebSocket.instances = [];
  }

  static get lastInstance(): MockWebSocket {
    return MockWebSocket.instances[MockWebSocket.instances.length - 1];
  }
}

interface HookRefs {
  wsRef: MutableRefObject<WebSocket | null>;
  subscribedExecutionsRef: MutableRefObject<Set<string>>;
  lastSequenceRef: MutableRefObject<number>;
}

function createRefs(): HookRefs {
  return {
    wsRef: { current: null } as MutableRefObject<WebSocket | null>,
    subscribedExecutionsRef: { current: new Set<string>() } as MutableRefObject<Set<string>>,
    lastSequenceRef: { current: 0 } as MutableRefObject<number>,
  };
}

function simulateOpen(ws: MockWebSocket): void {
  ws.readyState = MockWebSocket.OPEN;
  ws.onopen?.();
}

describe('useWebSocketConnection', () => {
  let setStatus: ReturnType<typeof vi.fn>;
  let processMessage: ReturnType<typeof vi.fn>;
  let trySseFallback: ReturnType<typeof vi.fn>;
  let getWebSocketUrl: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    MockWebSocket.reset();
    setStatus = vi.fn();
    processMessage = vi.fn();
    trySseFallback = vi.fn();
    getWebSocketUrl = vi.fn(() => 'ws://fake-url');
    vi.useFakeTimers();
    vi.stubGlobal('WebSocket', MockWebSocket);
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllGlobals();
    vi.clearAllMocks();
  });

  it('closeConnection_intentionalClose_doesNotReconnect', () => {
    const refs = createRefs();
    const { result } = renderHook(() =>
      useWebSocketConnection({
        ...refs,
        setLastSequence: vi.fn(),
        getWebSocketUrl,
        processMessage,
        trySseFallback,
        setStatus,
      }),
    );

    act(() => {
      result.current.connect();
    });
    expect(MockWebSocket.instanceCount).toBe(1);
    simulateOpen(MockWebSocket.lastInstance);

    // 模拟组件卸载的「主动关闭」路径
    act(() => {
      result.current.closeConnection();
    });

    // 浏览器异步触发 onclose（真实场景中 close() 后异步回调）
    act(() => {
      MockWebSocket.lastInstance.onclose?.({} as CloseEvent);
    });

    // 主动关闭仍应把状态置为 disconnected
    expect(setStatus).toHaveBeenCalledWith('disconnected');

    // 推进重连定时器，确认没有发起新的 WebSocket
    act(() => {
      vi.advanceTimersByTime(2200);
    });

    // 关键断言：主动关闭后不应再有任何重连（仍为同一个 WebSocket 实例）
    expect(MockWebSocket.instanceCount).toBe(1);
  });

  it('unexpectedClose_withoutCloseConnection_reconnects', () => {
    const refs = createRefs();
    const { result } = renderHook(() =>
      useWebSocketConnection({
        ...refs,
        setLastSequence: vi.fn(),
        getWebSocketUrl,
        processMessage,
        trySseFallback,
        setStatus,
      }),
    );

    act(() => {
      result.current.connect();
    });
    expect(MockWebSocket.instanceCount).toBe(1);
    simulateOpen(MockWebSocket.lastInstance);

    // 模拟意外断线：直接触发 onclose，绕过 closeConnection（manualClose 标志保持 false）
    act(() => {
      MockWebSocket.lastInstance.onclose?.({} as CloseEvent);
    });

    expect(setStatus).toHaveBeenCalledWith('disconnected');

    // 推进重连定时器（基础退避 2000ms），确认发起了重连
    act(() => {
      vi.advanceTimersByTime(2200);
    });

    expect(MockWebSocket.instanceCount).toBe(2);
  });
});

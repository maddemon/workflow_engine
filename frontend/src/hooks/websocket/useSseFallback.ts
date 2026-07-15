import { useCallback, useRef } from 'react';
import type { MutableRefObject } from 'react';
import type { WebSocketPushMessage, WebSocketStatus } from './messageHandlers.ts';

export interface UseSseFallbackOptions {
  getSseUrl: (executionId: string) => string;
  processMessage: (message: WebSocketPushMessage) => void;
  lastSequenceRef: MutableRefObject<number>;
  setLastSequence: (n: number) => void;
  setStatus: (status: WebSocketStatus) => void;
}

interface SseConnection {
  eventSource: EventSource;
  reconnectAttempts: number;
  reconnectTimeout: ReturnType<typeof setTimeout> | null;
}

export function useSseFallback(options: UseSseFallbackOptions) {
  const { getSseUrl, processMessage, lastSequenceRef, setLastSequence, setStatus } = options;
  const connectionsRef = useRef<Map<string, SseConnection>>(new Map());
  const maxReconnectAttempts = 5;
  const reconnectInterval = 2000;

  const closeConnection = useCallback((executionId: string) => {
    const conn = connectionsRef.current.get(executionId);
    if (conn) {
      if (conn.reconnectTimeout) {
        clearTimeout(conn.reconnectTimeout);
      }
      conn.eventSource.close();
      connectionsRef.current.delete(executionId);
    }
  }, []);

  const connectSse = useCallback((executionId: string, initialAttempts = 0) => {
    // 关闭已有连接
    closeConnection(executionId);

    const eventSource = new EventSource(getSseUrl(executionId));
    const conn: SseConnection = {
      eventSource,
      reconnectAttempts: initialAttempts,
      reconnectTimeout: null,
    };

    eventSource.onopen = () => {
      setStatus('connected');
      conn.reconnectAttempts = 0;
    };

    eventSource.onmessage = (event) => {
      try {
        const message = JSON.parse(event.data) as WebSocketPushMessage;
        lastSequenceRef.current = message.sequence;
        setLastSequence(message.sequence);
        processMessage(message);
      } catch {
        console.error('Failed to parse SSE message');
      }
    };

    eventSource.onerror = () => {
      eventSource.close();

      if (conn.reconnectAttempts < maxReconnectAttempts) {
        const currentAttempts = conn.reconnectAttempts;
        conn.reconnectTimeout = setTimeout(() => {
          const nextAttempts = currentAttempts + 1;
          if (connectionsRef.current.has(executionId)) {
            connectSse(executionId, nextAttempts);
          }
        }, reconnectInterval * Math.pow(2, currentAttempts));
      } else {
        setStatus('error');
        connectionsRef.current.delete(executionId);
      }
    };

    connectionsRef.current.set(executionId, conn);
  }, [getSseUrl, processMessage, lastSequenceRef, setLastSequence, setStatus, closeConnection]);

  const trySseFallback = useCallback((executionId: string) => {
    connectSse(executionId, 0);
  }, [connectSse]);

  const closeSse = useCallback(() => {
    for (const [executionId] of connectionsRef.current) {
      closeConnection(executionId);
    }
  }, [closeConnection]);

  const unsubscribeSse = useCallback((executionId: string) => {
    closeConnection(executionId);
  }, [closeConnection]);

  return { trySseFallback, closeSse, unsubscribeSse };
}

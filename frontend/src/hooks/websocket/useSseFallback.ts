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

export function useSseFallback(options: UseSseFallbackOptions) {
  const { getSseUrl, processMessage, lastSequenceRef, setLastSequence, setStatus } = options;
  const eventSourceRef = useRef<EventSource | null>(null);
  const reconnectAttemptsRef = useRef(0);
  const reconnectTimeoutRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const currentExecutionIdRef = useRef<string | null>(null);
  const connectSseRef = useRef<(executionId: string) => void>(() => {});
  const maxReconnectAttempts = 5;
  const reconnectInterval = 2000;

  const clearReconnectTimeout = useCallback(() => {
    if (reconnectTimeoutRef.current) {
      clearTimeout(reconnectTimeoutRef.current);
      reconnectTimeoutRef.current = null;
    }
  }, []);

  const connectSse = useCallback((executionId: string) => {
    eventSourceRef.current?.close();
    eventSourceRef.current = null;
    currentExecutionIdRef.current = executionId;

    const eventSource = new EventSource(getSseUrl(executionId));

    eventSource.onopen = () => {
      setStatus('connected');
      reconnectAttemptsRef.current = 0;
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
      eventSourceRef.current = null;

      if (reconnectAttemptsRef.current < maxReconnectAttempts && currentExecutionIdRef.current) {
        reconnectTimeoutRef.current = setTimeout(() => {
          reconnectAttemptsRef.current++;
          if (currentExecutionIdRef.current) {
            connectSseRef.current(currentExecutionIdRef.current);
          }
        }, reconnectInterval * Math.pow(2, reconnectAttemptsRef.current));
      } else {
        setStatus('error');
      }
    };

    eventSourceRef.current = eventSource;
  }, [getSseUrl, processMessage, lastSequenceRef, setLastSequence, setStatus]);

  // 保持 ref 与最新 connectSse 同步
  connectSseRef.current = connectSse;

  const trySseFallback = useCallback((executionId: string) => {
    clearReconnectTimeout();
    reconnectAttemptsRef.current = 0;
    connectSse(executionId);
  }, [connectSse, clearReconnectTimeout]);

  const closeSse = useCallback(() => {
    clearReconnectTimeout();
    currentExecutionIdRef.current = null;
    reconnectAttemptsRef.current = 0;
    if (eventSourceRef.current) {
      eventSourceRef.current.close();
      eventSourceRef.current = null;
    }
  }, [clearReconnectTimeout]);

  return { trySseFallback, closeSse };
}

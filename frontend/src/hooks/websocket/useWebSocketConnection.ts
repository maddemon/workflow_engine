import { useCallback, useEffect, useRef } from 'react';
import type { MutableRefObject } from 'react';
import type { WebSocketPushMessage, WebSocketStatus } from './messageHandlers.ts';

export interface UseWebSocketConnectionOptions {
  wsRef: MutableRefObject<WebSocket | null>;
  subscribedExecutionsRef: MutableRefObject<Set<string>>;
  lastSequenceRef: MutableRefObject<number>;
  setLastSequence: (n: number) => void;
  getWebSocketUrl: () => string;
  processMessage: (message: WebSocketPushMessage) => void;
  trySseFallback: (executionId: string) => void;
  setStatus: (status: WebSocketStatus) => void;
}

export function useWebSocketConnection(options: UseWebSocketConnectionOptions) {
  const {
    wsRef,
    subscribedExecutionsRef,
    lastSequenceRef,
    setLastSequence,
    getWebSocketUrl,
    processMessage,
    trySseFallback,
    setStatus,
  } = options;

  const reconnectTimeoutRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const reconnectAttemptsRef = useRef(0);
  const connectFnRef = useRef<() => void>(() => {});
  const maxReconnectAttempts = 5;
  const reconnectInterval = 2000;

  const doConnect = useCallback(() => {
    if (wsRef.current?.readyState === WebSocket.OPEN) {
      return;
    }

    setStatus('connecting');

    const ws = new WebSocket(getWebSocketUrl());

    ws.onopen = () => {
      setStatus('connected');
      reconnectAttemptsRef.current = 0;

      for (const executionId of subscribedExecutionsRef.current) {
        const seq = lastSequenceRef.current;
        ws.send(JSON.stringify({
          type: 'subscribe',
          executionId,
          lastSequence: seq > 0 ? seq : undefined,
        }));
      }
    };

    ws.onmessage = (event) => {
      try {
        const message = JSON.parse(event.data) as WebSocketPushMessage;
        lastSequenceRef.current = message.sequence;
        setLastSequence(message.sequence);
        processMessage(message);
      } catch {
        console.error('Failed to parse WebSocket message');
      }
    };

    ws.onclose = () => {
      setStatus('disconnected');
      wsRef.current = null;

      if (reconnectAttemptsRef.current < maxReconnectAttempts) {
        reconnectTimeoutRef.current = setTimeout(() => {
          reconnectAttemptsRef.current++;
          connectFnRef.current();
        }, reconnectInterval * Math.pow(2, reconnectAttemptsRef.current));
      } else if (subscribedExecutionsRef.current.size > 0) {
        const executionId = subscribedExecutionsRef.current.values().next().value as string;
        trySseFallback(executionId);
      }
    };

    ws.onerror = () => {
      setStatus('error');
    };

    wsRef.current = ws;
  }, [getWebSocketUrl, trySseFallback]);

  useEffect(() => {
    connectFnRef.current = doConnect;
  }, [doConnect]);

  const connect = useCallback(() => {
    doConnect();
  }, [doConnect]);

  const closeConnection = useCallback(() => {
    if (reconnectTimeoutRef.current) {
      clearTimeout(reconnectTimeoutRef.current);
      reconnectTimeoutRef.current = null;
    }

    if (wsRef.current) {
      // 只有连接已打开或正在打开时才关闭
      if (wsRef.current.readyState === WebSocket.OPEN || wsRef.current.readyState === WebSocket.CONNECTING) {
        wsRef.current.close(1000, 'Component unmounting');
      }
      wsRef.current = null;
    }

    reconnectAttemptsRef.current = 0;
  }, []);

  const subscribe = useCallback((executionId: string) => {
    subscribedExecutionsRef.current.add(executionId);

    if (wsRef.current?.readyState === WebSocket.OPEN) {
      const seq = lastSequenceRef.current;
      wsRef.current.send(JSON.stringify({
        type: 'subscribe',
        executionId,
        lastSequence: seq > 0 ? seq : undefined,
      }));
    }
  }, []);

  const unsubscribe = useCallback((executionId: string) => {
    subscribedExecutionsRef.current.delete(executionId);

    if (wsRef.current?.readyState === WebSocket.OPEN) {
      wsRef.current.send(JSON.stringify({
        type: 'unsubscribe',
        executionId,
      }));
    }
  }, []);

  return { connect, closeConnection, subscribe, unsubscribe };
}

import { useCallback, useEffect, useRef, useState } from 'react';
import { notifications } from '@mantine/notifications';
import { useCanvasStore } from '../components/Canvas/stores/canvasStore.ts';
import type { ExecutionDto } from '../types/workflow.ts';
import { getExecutionStreamUrl as buildSseUrl, getWebSocketUrl as buildWsUrl } from '../services/api.ts';
import { messageHandlers, type WebSocketPushMessage, type WebSocketStatus } from './websocket/messageHandlers.ts';
import { useWebSocketConnection } from './websocket/useWebSocketConnection.ts';
import { useSseFallback } from './websocket/useSseFallback.ts';

interface UseWebSocketExecutionOptions {
  updateExecutionMeta: (updater: (prev: ExecutionDto | null) => ExecutionDto | null) => void;
}

export function useWebSocketExecution(options: UseWebSocketExecutionOptions) {
  const { updateExecutionMeta } = options;
  const [status, setStatus] = useState<WebSocketStatus>('disconnected');
  const [lastSequence, setLastSequence] = useState(0);
  const lastSequenceRef = useRef(0);
  const wsRef = useRef<WebSocket | null>(null);
  const subscribedExecutionsRef = useRef<Set<string>>(new Set());

  const getWebSocketUrl = useCallback(() => {
    // CQ-3：地址构造集中到 services/api.ts 的 getWebSocketUrl。
    return buildWsUrl();
  }, []);

  const getSseUrl = useCallback(
    (executionId: string) => {
      // CQ-3：SSE 流地址集中到 services/api.ts 的 getExecutionStreamUrl（同源 /api/v1）。
      return buildSseUrl(executionId);
    },
    [],
  );

  const sendIfOpen = useCallback((data: string) => {
    if (wsRef.current?.readyState === WebSocket.OPEN) {
      wsRef.current.send(data);
    }
  }, []);

  const processMessage = useCallback((message: WebSocketPushMessage) => {
    const handler = messageHandlers[message.type];
    handler?.(message, {
      store: useCanvasStore.getState(),
      notifications,
      sendIfOpen,
      updateExecutionMeta,
    });
  }, [sendIfOpen, updateExecutionMeta]);

  const { trySseFallback, closeSse, unsubscribeSse } = useSseFallback({
    getSseUrl,
    processMessage,
    lastSequenceRef,
    setLastSequence,
    setStatus,
  });

  const { connect, closeConnection, subscribe, unsubscribe } = useWebSocketConnection({
    wsRef,
    subscribedExecutionsRef,
    lastSequenceRef,
    setLastSequence,
    getWebSocketUrl,
    processMessage,
    trySseFallback,
    setStatus,
  });

  const disconnect = useCallback(() => {
    closeConnection();
    closeSse();
    setStatus('disconnected');
    subscribedExecutionsRef.current.clear();
  }, [closeConnection, closeSse]);

  const unsubscribeWithSse = useCallback((executionId: string) => {
    unsubscribe(executionId);
    unsubscribeSse(executionId);
  }, [unsubscribe, unsubscribeSse]);

  useEffect(() => {
    return () => {
      disconnect();
    };
  }, [disconnect]);

  return {
    status,
    lastSequence,
    connect,
    disconnect,
    subscribe,
    unsubscribe: unsubscribeWithSse,
  };
}

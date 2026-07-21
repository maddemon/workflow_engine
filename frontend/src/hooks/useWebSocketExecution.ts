import { useCallback, useEffect, useRef, useState } from 'react';
import { notifications } from '@mantine/notifications';
import { useCanvasStore } from '../components/Canvas/stores/canvasStore.ts';
import type { ExecutionDto } from '../types/workflow.ts';
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
    const protocol = window.location.protocol === 'https:' ? 'wss:' : 'ws:';
    const host = window.location.host;
    // H5：JWT 不再经 URL query 暴露；同源 WS 自动携带后端下发的 HttpOnly Cookie（fe_auth），
    // 由后端 JwtBearer.OnMessageReceived 读取，无需在 URL 中拼接令牌。
    return `${protocol}//${host}/ws/execution`;
  }, []);

  const getSseUrl = useCallback((executionId: string) => {
    // H5：SSE 受 EventSource 限制无法自定义头；同源请求自动携带 HttpOnly Cookie（fe_auth），
    // 由后端 JwtBearer.OnMessageReceived 读取，移除 query 方式暴露令牌。
    return `/api/v1/executions/${executionId}/stream`;
  }, []);

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

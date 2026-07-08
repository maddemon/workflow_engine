import { useCallback, useEffect, useRef, useState } from 'react';
import { notifications } from '@mantine/notifications';
import { useWorkflowStore } from '../stores/workflowStore.ts';
import type { NodeExecutionRecordDto } from '../types/workflow.ts';

type WebSocketStatus = 'connecting' | 'connected' | 'disconnected' | 'error';

interface WebSocketPushMessage {
  type: string;
  executionId: string;
  timestamp: string;
  sequence: number;
  payload?: {
    workflowDefinitionId?: string;
    nodeDefinitionId?: string;
    runIndex?: number;
    result?: {
      success: boolean;
      itemCount: number;
      error?: { code: string; message: string };
    };
    error?: { code: string; message: string };
    finalStatus?: string;
    eventType?: string;
  };
}

export function useWebSocketExecution() {
  const [status, setStatus] = useState<WebSocketStatus>('disconnected');
  const [lastSequence, setLastSequence] = useState(0);
  const lastSequenceRef = useRef(0);
  const wsRef = useRef<WebSocket | null>(null);
  const eventSourceRef = useRef<EventSource | null>(null);
  const reconnectTimeoutRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const subscribedExecutionsRef = useRef<Set<string>>(new Set());
  const reconnectAttemptsRef = useRef(0);
  const connectFnRef = useRef<() => void>(() => {});
  const maxReconnectAttempts = 5;
  const reconnectInterval = 2000;

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

  const processMessage = useCallback((message: WebSocketPushMessage) => {
    const store = useWorkflowStore.getState();

    switch (message.type) {
      case 'execution_started':
        if (message.payload?.workflowDefinitionId) {
          store.setIsExecuting(true);
        }
        break;

      case 'node_started':
        if (message.payload?.nodeDefinitionId) {
          const nodeDefId = message.payload.nodeDefinitionId;
          store.updateNodeExecutionStatus(nodeDefId, 'running');

          // 更新节点的 startedAt 时间
          const existingRecord = store.nodeExecutionRecords[nodeDefId];
          if (existingRecord) {
            store.upsertNodeExecutionRecords([{
              ...existingRecord,
              startedAt: message.timestamp,
            }]);
          } else {
            // 创建临时记录，等 node_executed 事件补充完整信息
            const tempRecord: NodeExecutionRecordDto = {
              id: `${nodeDefId}-${message.payload.runIndex ?? 0}`,
              nodeDefinitionId: nodeDefId,
              runIndex: message.payload.runIndex ?? 0,
              status: 'Running',
              startedAt: message.timestamp,
              completedAt: null,
              inputs: null,
              output: null,
              rawParameters: null,
              resolvedParameters: null,
            };
            store.upsertNodeExecutionRecords([tempRecord]);
          }
        }
        break;

      case 'node_executed':
        if (message.payload?.nodeDefinitionId && message.payload?.result) {
          const { nodeDefinitionId, result } = message.payload;
          const status = result.success ? 'success' : 'error';
          store.updateNodeExecutionStatus(nodeDefinitionId, status);

          // 使用已有的 startedAt（来自 node_started 事件），如果不存在则使用 message.timestamp
          const existingRecord = store.nodeExecutionRecords[nodeDefinitionId];
          const startedAt = existingRecord?.startedAt ?? message.timestamp;

          const record: NodeExecutionRecordDto = {
            id: `${nodeDefinitionId}-${message.payload.runIndex ?? 0}`,
            nodeDefinitionId,
            runIndex: message.payload.runIndex ?? 0,
            status: result.success ? 'Completed' : 'Failed',
            startedAt,
            completedAt: message.timestamp,
            inputs: null,
            output: result,
            rawParameters: null,
            resolvedParameters: null,
          };
          store.upsertNodeExecutionRecords([record]);
        }
        break;

      case 'node_error':
        if (message.payload?.nodeDefinitionId && message.payload?.error) {
          const { nodeDefinitionId, error } = message.payload;
          store.updateNodeExecutionStatus(nodeDefinitionId, 'error');

          // 使用已有的 startedAt（来自 node_started 事件），如果不存在则使用 message.timestamp
          const existingRecord = store.nodeExecutionRecords[nodeDefinitionId];
          const startedAt = existingRecord?.startedAt ?? message.timestamp;

          const record: NodeExecutionRecordDto = {
            id: `${nodeDefinitionId}-${message.payload.runIndex ?? 0}`,
            nodeDefinitionId,
            runIndex: message.payload.runIndex ?? 0,
            status: 'Failed',
            startedAt,
            completedAt: message.timestamp,
            inputs: null,
            output: { error },
            rawParameters: null,
            resolvedParameters: null,
          };
          store.upsertNodeExecutionRecords([record]);
        }
        break;

      case 'execution_completed':
        store.setIsExecuting(false);
        if (message.payload?.finalStatus === 'Completed') {
          notifications.show({
            title: 'Execution Complete',
            message: 'Workflow execution completed successfully.',
            color: 'green',
          });
        }
        break;

      case 'execution_failed':
        store.setIsExecuting(false);
        notifications.show({
          title: 'Execution Failed',
          message: message.payload?.error?.message ?? 'Workflow execution failed.',
          color: 'red',
        });
        break;

      case 'execution_cancelled':
        store.setIsExecuting(false);
        notifications.show({
          title: 'Execution Cancelled',
          message: 'Workflow execution was cancelled.',
          color: 'yellow',
        });
        break;

      case 'pong':
        break;

      case 'ping':
        if (wsRef.current?.readyState === WebSocket.OPEN) {
          wsRef.current.send(JSON.stringify({ type: 'pong' }));
        }
        break;
    }
  }, []);

  const trySseFallback = useCallback((executionId: string) => {
    eventSourceRef.current?.close();
    eventSourceRef.current = null;

    const eventSource = new EventSource(getSseUrl(executionId));
    setStatus('connected');

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
      setStatus('error');
      eventSource.close();
      eventSourceRef.current = null;
    };

    eventSourceRef.current = eventSource;
  }, [getSseUrl, processMessage]);

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

  const disconnect = useCallback(() => {
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

    if (eventSourceRef.current) {
      eventSourceRef.current.close();
      eventSourceRef.current = null;
    }

    setStatus('disconnected');
    subscribedExecutionsRef.current.clear();
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
    unsubscribe,
  };
}

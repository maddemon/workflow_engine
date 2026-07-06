import { useState, useCallback, useEffect, useRef } from 'react';
import { executeWorkflow, getWorkflowExecutions, getExecution, cancelExecution as apiCancelExecution } from '../services/api.ts';
import { useWorkflowStore } from '../stores/workflowStore.ts';
import { useWebSocketExecution } from './useWebSocketExecution.ts';
import type { ExecutionDto, NodeExecutionRecordDto } from '../types/workflow.ts';

type ExecutionHookStatus = 'idle' | 'loading' | 'running' | 'completed' | 'failed';

function applyNodeStatuses(records: NodeExecutionRecordDto[]) {
  const store = useWorkflowStore.getState();
  for (const r of records) {
    const mapped: Record<string, typeof store.nodes[0]['data']['executionStatus']> = {
      Pending: 'waiting',
      Running: 'running',
      Completed: 'success',
      Failed: 'error',
      Cancelled: 'error',
    };
    store.updateNodeExecutionStatus(r.nodeDefinitionId, mapped[r.status] ?? 'idle');
  }
}

const TERMINAL_STATUSES = new Set(['Completed', 'Failed', 'Cancelled']);

export function useExecution() {
  const [executionMeta, setExecutionMeta] = useState<ExecutionDto | null>(null);
  const [status, setStatus] = useState<ExecutionHookStatus>('idle');
  const [error, setError] = useState<string | null>(null);
  const { subscribe, unsubscribe, connect, disconnect } = useWebSocketExecution();
  const pollingRef = useRef<ReturnType<typeof setInterval> | null>(null);

  useEffect(() => {
    connect();
    return () => disconnect();
  }, [connect, disconnect]);

  // 轮询执行状态（WebSocket 的兜底方案）
  const startPolling = useCallback((executionId: string) => {
    stopPolling();
    pollingRef.current = setInterval(async () => {
      try {
        const latest = await getExecution(executionId);
        if (TERMINAL_STATUSES.has(latest.status)) {
          stopPolling();
          setExecutionMeta(latest);
          if (latest.nodeRecords && latest.nodeRecords.length > 0) {
            useWorkflowStore.getState().upsertNodeExecutionRecords(latest.nodeRecords);
            applyNodeStatuses(latest.nodeRecords);
          }
          setStatus(latest.status === 'Completed' ? 'completed' : 'failed');
          useWorkflowStore.getState().setIsExecuting(false);
        }
      } catch {
        // 忽略轮询错误
      }
    }, 2000);
  }, []);

  const stopPolling = useCallback(() => {
    if (pollingRef.current) {
      clearInterval(pollingRef.current);
      pollingRef.current = null;
    }
  }, []);

  useEffect(() => {
    return () => stopPolling();
  }, [stopPolling]);

  // 页面加载时检查是否有正在运行的执行，并自动订阅
  useEffect(() => {
    const workflowId = useWorkflowStore.getState().workflowId;
    if (!workflowId) return;

    const checkRunningExecutions = async () => {
      try {
        const executions = await getWorkflowExecutions(workflowId);
        // 找到正在运行的执行（Pending 或 Running 状态）
        const runningExecution = executions.find(
          (e) => e.status === 'Pending' || e.status === 'Running'
        );

        if (runningExecution) {
          // 获取完整的执行详情（包含 nodeRecords）
          const detailedExecution = await getExecution(runningExecution.id);
          setExecutionMeta(detailedExecution);
          setStatus('running');
          useWorkflowStore.getState().setIsExecuting(true);

          // 订阅该执行的 WebSocket 事件
          subscribe(runningExecution.id);
          // 启动轮询作为兜底
          startPolling(runningExecution.id);

          // 如果有节点执行记录，应用它们
          if (detailedExecution.nodeRecords && detailedExecution.nodeRecords.length > 0) {
            useWorkflowStore.getState().upsertNodeExecutionRecords(detailedExecution.nodeRecords);
            applyNodeStatuses(detailedExecution.nodeRecords);
          }
        }
      } catch (err) {
        console.error('Failed to check running executions:', err);
      }
    };

    checkRunningExecutions();
  }, [subscribe, startPolling]);

  const execute = useCallback(
    async (workflowId: string) => {
      setStatus('loading');
      setError(null);
      const store = useWorkflowStore.getState();
      store.setIsExecuting(true);
      store.clearExecutionStatuses();
      store.clearNodeExecutionRecords();

      try {
        const result = await executeWorkflow(workflowId);
        setExecutionMeta(result);
        if (result.nodeRecords.length > 0) {
          store.upsertNodeExecutionRecords(result.nodeRecords);
          applyNodeStatuses(result.nodeRecords);
        }

        subscribe(result.id);

        if (result.status === 'Completed') {
          setStatus('completed');
          store.setIsExecuting(false);
        } else if (result.status === 'Failed' || result.status === 'Cancelled') {
          setStatus('failed');
          store.setIsExecuting(false);
        } else {
          setStatus('running');
          // 启动轮询作为兜底
          startPolling(result.id);
        }
      } catch (err) {
        setStatus('failed');
        store.setIsExecuting(false);
        const message = err instanceof Error ? err.message : 'Execution failed';
        setError(message);
      }
    },
    [subscribe, startPolling],
  );

  const clearExecution = useCallback(() => {
    stopPolling();
    if (executionMeta) {
      unsubscribe(executionMeta.id);
    }
    setExecutionMeta(null);
    setStatus('idle');
    setError(null);
    useWorkflowStore.getState().setIsExecuting(false);
    useWorkflowStore.getState().clearExecutionStatuses();
    useWorkflowStore.getState().clearNodeExecutionRecords();
  }, [executionMeta, unsubscribe, stopPolling]);

  const cancelExecution = useCallback(async () => {
    if (!executionMeta) return;
    try {
      await apiCancelExecution(executionMeta.id);
      setStatus('failed');
      useWorkflowStore.getState().setIsExecuting(false);
      stopPolling();
    } catch (err: any) {
      // 409 = 执行已结束，获取最新状态
      if (err?.response?.status === 409) {
        stopPolling();
        try {
          const latest = await getExecution(executionMeta.id);
          setExecutionMeta(latest);
          if (latest.nodeRecords && latest.nodeRecords.length > 0) {
            useWorkflowStore.getState().upsertNodeExecutionRecords(latest.nodeRecords);
            applyNodeStatuses(latest.nodeRecords);
          }
          setStatus(latest.status === 'Completed' ? 'completed' : 'failed');
        } catch {
          setStatus('completed');
        }
        useWorkflowStore.getState().setIsExecuting(false);
      } else {
        console.error('Failed to cancel execution:', err);
      }
    }
  }, [executionMeta, stopPolling]);

  return { execution: executionMeta, status, error, execute, clearExecution, cancelExecution };
}

export type { ExecutionHookStatus };

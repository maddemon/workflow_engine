import { useState, useCallback, useEffect } from 'react';
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

export function useExecution() {
  const [executionMeta, setExecutionMeta] = useState<ExecutionDto | null>(null);
  const [status, setStatus] = useState<ExecutionHookStatus>('idle');
  const [error, setError] = useState<string | null>(null);
  const { subscribe, unsubscribe, connect, disconnect } = useWebSocketExecution();

  useEffect(() => {
    connect();
    return () => disconnect();
  }, [connect, disconnect]);

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
  }, [subscribe]);

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
        }
      } catch (err) {
        setStatus('failed');
        store.setIsExecuting(false);
        const message = err instanceof Error ? err.message : 'Execution failed';
        setError(message);
      }
    },
    [subscribe],
  );

  const clearExecution = useCallback(() => {
    if (executionMeta) {
      unsubscribe(executionMeta.id);
    }
    setExecutionMeta(null);
    setStatus('idle');
    setError(null);
    useWorkflowStore.getState().setIsExecuting(false);
    useWorkflowStore.getState().clearExecutionStatuses();
    useWorkflowStore.getState().clearNodeExecutionRecords();
  }, [executionMeta, unsubscribe]);

  const cancelExecution = useCallback(async () => {
    if (!executionMeta) return;
    try {
      await apiCancelExecution(executionMeta.id);
      setStatus('failed');
      useWorkflowStore.getState().setIsExecuting(false);
    } catch (err) {
      console.error('Failed to cancel execution:', err);
    }
  }, [executionMeta]);

  return { execution: executionMeta, status, error, execute, clearExecution, cancelExecution };
}

export type { ExecutionHookStatus };

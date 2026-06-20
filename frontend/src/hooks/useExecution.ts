import { useState, useCallback, useRef, useEffect } from 'react';
import { notifications } from '@mantine/notifications';
import { executeWorkflow, getExecution } from '../services/api.ts';
import { useWorkflowStore } from '../stores/workflowStore.ts';
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
  const pollingRef = useRef<ReturnType<typeof setInterval> | null>(null);

  const stopPolling = useCallback(() => {
    if (pollingRef.current) {
      clearInterval(pollingRef.current);
      pollingRef.current = null;
    }
  }, []);

  useEffect(() => {
    return () => {
      if (pollingRef.current) {
        clearInterval(pollingRef.current);
        pollingRef.current = null;
      }
    };
  }, []);

  const pollExecution = useCallback(
    (executionId: string) => {
      stopPolling();
      pollingRef.current = setInterval(async () => {
        try {
          const updated = await getExecution(executionId);
          setExecutionMeta((prev) => prev ? { ...prev, status: updated.status, startedAt: updated.startedAt, completedAt: updated.completedAt } : prev);
          if (updated.nodeRecords.length > 0) {
            useWorkflowStore.getState().upsertNodeExecutionRecords(updated.nodeRecords);
            applyNodeStatuses(updated.nodeRecords);
          }
          if (updated.status === 'Completed') {
            setStatus('completed');
            stopPolling();
            useWorkflowStore.getState().setIsExecuting(false);
          } else if (updated.status === 'Failed' || updated.status === 'Cancelled') {
            setStatus('failed');
            stopPolling();
            useWorkflowStore.getState().setIsExecuting(false);
          }
        } catch {
          setStatus('failed');
          stopPolling();
          useWorkflowStore.getState().setIsExecuting(false);
          notifications.show({ title: 'Polling Error', message: 'Failed to fetch execution status.', color: 'red' });
        }
      }, 1000);
    },
    [stopPolling],
  );

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
        if (result.status === 'Completed') {
          setStatus('completed');
          store.setIsExecuting(false);
        } else if (result.status === 'Failed' || result.status === 'Cancelled') {
          setStatus('failed');
          store.setIsExecuting(false);
        } else {
          setStatus('running');
          pollExecution(result.id);
        }
      } catch (err) {
        setStatus('failed');
        store.setIsExecuting(false);
        const message = err instanceof Error ? err.message : 'Execution failed';
        setError(message);
      }
    },
    [pollExecution],
  );

  const clearExecution = useCallback(() => {
    stopPolling();
    setExecutionMeta(null);
    setStatus('idle');
    setError(null);
    useWorkflowStore.getState().setIsExecuting(false);
    useWorkflowStore.getState().clearExecutionStatuses();
    useWorkflowStore.getState().clearNodeExecutionRecords();
  }, [stopPolling]);

  return { execution: executionMeta, status, error, execute, clearExecution };
}

export type { ExecutionHookStatus };

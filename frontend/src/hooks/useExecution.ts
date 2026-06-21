import { useState, useCallback, useEffect } from 'react';
import { executeWorkflow } from '../services/api.ts';
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

  return { execution: executionMeta, status, error, execute, clearExecution };
}

export type { ExecutionHookStatus };

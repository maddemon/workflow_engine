import { useState, useCallback, useRef, useEffect } from 'react';
import { executeWorkflow, getExecution } from '../services/api.ts';
import type { ExecutionDto } from '../types/workflow.ts';

type ExecutionHookStatus = 'idle' | 'loading' | 'running' | 'completed' | 'failed';

export function useExecution() {
  const [execution, setExecution] = useState<ExecutionDto | null>(null);
  const [status, setStatus] = useState<ExecutionHookStatus>('idle');
  const [error, setError] = useState<string | null>(null);
  const pollingRef = useRef<ReturnType<typeof setInterval> | null>(null);

  const stopPolling = useCallback(() => {
    if (pollingRef.current) {
      clearInterval(pollingRef.current);
      pollingRef.current = null;
    }
  }, []);

  // 组件卸载时清理轮询
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
          setExecution(updated);
          if (updated.status === 'Completed') {
            setStatus('completed');
            stopPolling();
          } else if (updated.status === 'Failed' || updated.status === 'Cancelled') {
            setStatus('failed');
            stopPolling();
          }
        } catch {
          setStatus('failed');
          stopPolling();
        }
      }, 1500);
    },
    [stopPolling],
  );

  const execute = useCallback(
    async (workflowId: string) => {
      setStatus('loading');
      setError(null);
      try {
        const result = await executeWorkflow(workflowId);
        setExecution(result);
        if (result.status === 'Completed') {
          setStatus('completed');
        } else if (result.status === 'Failed' || result.status === 'Cancelled') {
          setStatus('failed');
        } else {
          setStatus('running');
          pollExecution(result.id);
        }
      } catch (err) {
        setStatus('failed');
        setError(err instanceof Error ? err.message : 'Execution failed');
      }
    },
    [pollExecution],
  );

  const clearExecution = useCallback(() => {
    stopPolling();
    setExecution(null);
    setStatus('idle');
    setError(null);
  }, [stopPolling]);

  return { execution, status, error, execute, clearExecution };
}

export type { ExecutionHookStatus };

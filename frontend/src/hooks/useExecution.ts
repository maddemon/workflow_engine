import { useState, useCallback, useEffect, useRef } from 'react';
import { notifications } from '@mantine/notifications';
import { executeWorkflow, getActiveExecutions, getExecution, cancelExecution as apiCancelExecution, dryRun as apiDryRun } from '../services/api.ts';
import { serializeWorkflow } from '../utils/workflowSerializer.ts';
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
const POLLING_INTERVAL_MS = 2000;

export function useExecution() {
  const [executionMeta, setExecutionMeta] = useState<ExecutionDto | null>(null);
  const [status, setStatus] = useState<ExecutionHookStatus>('idle');
  const [error, setError] = useState<string | null>(null);
  const [dryRunLoading, setDryRunLoading] = useState(false);

  const updateExecutionMeta = useCallback(
    (updater: (prev: ExecutionDto | null) => ExecutionDto | null) => {
      setExecutionMeta(updater);
    },
    [],
  );

  const { subscribe, unsubscribe, connect, disconnect } = useWebSocketExecution({ updateExecutionMeta });
  const pollingRef = useRef<ReturnType<typeof setInterval> | null>(null);

  const stopPolling = useCallback(() => {
    if (pollingRef.current) {
      clearInterval(pollingRef.current);
      pollingRef.current = null;
    }
  }, []);

  // 轮询执行状态（WebSocket 的兜底方案）
  const startPolling = useCallback((executionId: string) => {
    stopPolling();
    let cancelled = false;
    pollingRef.current = setInterval(async () => {
      try {
        const latest = await getExecution(executionId);
        if (cancelled) return;
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
    }, POLLING_INTERVAL_MS);

    return () => {
      cancelled = true;
      stopPolling();
    };
  }, [stopPolling]);

  useEffect(() => {
    connect();
    return () => disconnect();
  }, [connect, disconnect]);

  useEffect(() => {
    return () => stopPolling();
  }, [stopPolling]);

  // 页面加载时检查是否有正在运行的执行，并自动订阅
  const workflowId = useWorkflowStore((s) => s.workflowId);

  useEffect(() => {
    if (!workflowId) return;

    let cancelled = false;
    let cleanupPolling: (() => void) | null = null;

    const checkRunningExecutions = async () => {
      try {
        // 端点已仅返回活跃执行（Pending/Running），此处取第一条即为当前运行中的执行。
        const executions = await getActiveExecutions(workflowId);
        if (cancelled) return;

        const runningExecution = executions.find(
          (e) => e.status === 'Pending' || e.status === 'Running'
        );

        if (runningExecution) {
          // 获取完整的执行详情（包含 nodeRecords）
          const detailedExecution = await getExecution(runningExecution.id);
          if (cancelled) return;

          setExecutionMeta(detailedExecution);
          setStatus('running');
          useWorkflowStore.getState().setIsExecuting(true);

          // 订阅该执行的 WebSocket 事件
          subscribe(runningExecution.id);
          // 启动轮询作为兜底
          cleanupPolling = startPolling(runningExecution.id);

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

    return () => {
      cancelled = true;
      cleanupPolling?.();
    };
  }, [workflowId, subscribe, startPolling]);

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
        if (result.nodeRecords && result.nodeRecords.length > 0) {
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
    } catch (err: unknown) {
      // 409 = 执行已结束，获取最新状态
      const axiosErr = err as { response?: { status?: number } } | undefined;
      if (axiosErr?.response?.status === 409) {
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

  const dryRun = useCallback(async () => {
    const store = useWorkflowStore.getState();
    store.clearNodeExecutionRecords();
    store.clearExecutionStatuses();
    setError(null);
    setDryRunLoading(true);
    try {
      if (!store.validateAllNodes()) {
        const msg = '请先修正节点配置错误后再试运行。';
        setError(msg);
        notifications.show({ title: 'Dry Run', message: msg, color: 'red', autoClose: 3000 });
        return;
      }
      const { nodeDefinitions, connections } = serializeWorkflow(store.nodes, store.edges, store.workflowName);
      if (nodeDefinitions.length === 0) {
        const msg = '请先添加节点后再试运行。';
        setError(msg);
        notifications.show({ title: 'Dry Run', message: msg, color: 'red', autoClose: 3000 });
        return;
      }
      const result = await apiDryRun({ nodes: nodeDefinitions, connections });
      setExecutionMeta(result);
      if (result.nodeRecords && result.nodeRecords.length > 0) {
        store.upsertNodeExecutionRecords(result.nodeRecords);
        applyNodeStatuses(result.nodeRecords);
      }
      const success = result.status === 'Completed' || result.status === 'DryRunCompleted';
      setStatus(success ? 'completed' : 'failed');
      if (success) {
        notifications.show({
          title: 'Dry Run',
          message: '模拟执行完成，所有节点验证通过',
          color: 'green',
          autoClose: 3000,
        });
      } else {
        const failedNodes = (result.nodeRecords ?? [])
          .filter((r) => r.status === 'Failed')
          .map((r) => {
            const err = (r.output as Record<string, unknown>)?.error as { code?: string; message?: string } | undefined;
            return `${r.nodeDefinitionId}: ${err?.message ?? '未知错误'}`;
          });
        notifications.show({
          title: 'Dry Run 失败',
          message: failedNodes.length > 0 ? failedNodes.join('\n') : '请检查节点配置',
          color: 'red',
          autoClose: 8000,
        });
      }
    } catch (err) {
      const msg = err instanceof Error ? err.message : 'Dry-run 失败';
      setError(msg);
      notifications.show({
        title: 'Dry Run',
        message: msg,
        color: 'red',
        autoClose: 5000,
      });
    } finally {
      setDryRunLoading(false);
    }
  }, []);

  return { execution: executionMeta, status, error, execute, dryRun, dryRunLoading, clearExecution, cancelExecution };
}

export type { ExecutionHookStatus };

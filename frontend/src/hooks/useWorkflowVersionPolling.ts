import { useCallback, useEffect, useRef, useState } from 'react';
import { getWorkflowVersion } from '../services/api.ts';
import { useWorkflowStore } from '../stores/workflowStore.ts';
import { useCanvasStore } from '../components/Canvas/stores/canvasStore.ts';

interface UseWorkflowVersionPollingResult {
  changed: boolean;
  newVersion: number | null;
  dismiss: () => void;
}

export function useWorkflowVersionPolling(workflowId: string | null): UseWorkflowVersionPollingResult {
  const storeVersion = useWorkflowStore((s) => s.workflowVersion);
  const isExecuting = useCanvasStore((s) => s.isExecuting);
  const reviewMode = useCanvasStore((s) => s.reviewMode);

  const [changed, setChanged] = useState(false);
  const [newVersion, setNewVersion] = useState<number | null>(null);
  const intervalRef = useRef<ReturnType<typeof setInterval> | null>(null);
  const latestVersionRef = useRef(storeVersion);

  // Keep ref in sync with store
  useEffect(() => {
    latestVersionRef.current = storeVersion;
  }, [storeVersion]);

  useEffect(() => {
    // Only poll when we have a valid workflow ID and editor is not in review/executing state
    if (!workflowId || workflowId === 'new' || reviewMode || isExecuting) {
      if (intervalRef.current) {
        clearInterval(intervalRef.current);
        intervalRef.current = null;
      }
      return;
    }

    intervalRef.current = setInterval(async () => {
      try {
        const info = await getWorkflowVersion(workflowId!);
        if (info.version > latestVersionRef.current) {
          setChanged(true);
          setNewVersion(info.version);
        }
      } catch {
        // Silently ignore polling errors
      }
    }, 30000);

    return () => {
      if (intervalRef.current) {
        clearInterval(intervalRef.current);
        intervalRef.current = null;
      }
    };
  }, [workflowId, reviewMode, isExecuting]);

  const dismiss = useCallback(() => {
    // 推进基线版本到被忽略的版本，避免下一轮轮询用同一高版本重复提示。
    // 注意：dismiss 不是保存，不更新全局 store 的 workflowVersion。
    latestVersionRef.current = newVersion ?? latestVersionRef.current;
    setChanged(false);
    setNewVersion(null);
  }, [newVersion]);

  return { changed, newVersion, dismiss };
}

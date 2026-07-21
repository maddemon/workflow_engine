import { useCallback, useEffect, useRef, useState } from 'react';
import { getWorkflow } from '../services/api.ts';
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
        const workflow = await getWorkflow(workflowId!);
        if (workflow.version > latestVersionRef.current) {
          setChanged(true);
          setNewVersion(workflow.version);
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
    setChanged(false);
    setNewVersion(null);
  }, []);

  return { changed, newVersion, dismiss };
}

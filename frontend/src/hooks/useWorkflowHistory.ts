import { useCallback } from 'react';
import { useWorkflowStore } from '../stores/workflowStore.ts';

/**
 * 操作历史管理 hook，支持 undo/redo。
 * 按计划 plan-mvp-09 阶段四实现。
 * 历史栈由 store 内部管理，本 hook 仅暴露 API。
 */
export function useWorkflowHistory() {
  const canUndo = useWorkflowStore((s) => s.canUndo);
  const canRedo = useWorkflowStore((s) => s.canRedo);
  const undo = useWorkflowStore((s) => s.undo);
  const redo = useWorkflowStore((s) => s.redo);
  const pushHistory = useWorkflowStore((s) => s.pushHistory);

  const pushSnapshot = useCallback(() => {
    pushHistory();
  }, [pushHistory]);

  return { undo, redo, canUndo, canRedo, pushSnapshot };
}
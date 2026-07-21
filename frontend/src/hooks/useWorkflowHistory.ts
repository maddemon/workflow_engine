import { useCallback } from 'react';
import { useCanvasStore } from '../components/Canvas/stores/canvasStore.ts';

/**
 * 操作历史管理 hook，支持 undo/redo。
 * 按计划 plan-mvp-09 阶段四实现。
 * 历史栈由画布 store 内部管理，本 hook 仅暴露 API。
 */
export function useWorkflowHistory() {
  const canUndo = useCanvasStore((s) => s.canUndo);
  const canRedo = useCanvasStore((s) => s.canRedo);
  const undo = useCanvasStore((s) => s.undo);
  const redo = useCanvasStore((s) => s.redo);
  const pushHistory = useCanvasStore((s) => s.pushHistory);

  const pushSnapshot = useCallback(() => {
    pushHistory();
  }, [pushHistory]);

  return { undo, redo, canUndo, canRedo, pushSnapshot };
}
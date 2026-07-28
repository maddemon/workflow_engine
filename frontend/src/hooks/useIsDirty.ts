import { useWorkflowStore } from '../stores/workflowStore.ts';
import { useCanvasStore } from '../components/Canvas/stores/canvasStore.ts';

/** 工作流是否脏（画布变更或工作流元信息变更任一为真即为脏）。 */
export function useIsDirty(): boolean {
  const workflowDirty = useWorkflowStore((s) => s.isDirty);
  const canvasDirty = useCanvasStore((s) => s.isDirty);
  return workflowDirty || canvasDirty;
}

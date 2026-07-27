import { create } from 'zustand';
import type { StructuredDiff, WorkflowStyleSettings } from '../types/workflow.ts';
import { DEFAULT_STYLE_SETTINGS } from '../types/workflow.ts';
import { deserializeWorkflow, serializeWorkflow } from '../utils/workflowSerializer.ts';
import { computeAutoLayout } from '../utils/workflowLayout.ts';
import * as api from '../services/api.ts';
import { useCanvasStore } from '../components/Canvas/stores/canvasStore.ts';
import { notifications } from '@mantine/notifications';
import i18n from '../i18n.ts';

// 画布相关类型（WorkflowNode/WorkflowEdge/WorkflowNodeData）定义在 canvasStore，
// 此处重新导出以保持既有导入路径兼容。
export type { WorkflowNode, WorkflowEdge, WorkflowNodeData } from '../components/Canvas/stores/canvasStore.ts';

interface WorkflowState {
  workflowId: string | null;
  projectId: string | null;
  workflowName: string;
  workflowVersion: number;
  isActive: boolean;
  isDirty: boolean;
  saving: boolean;
  /** 凭据变更版本号，用于跨组件触发凭据下拉刷新 */
  credentialRevision: number;
  draftSource?: 'Ai' | 'Human';
  draftStatus?: 'Pending' | 'Rejected' | 'Confirmed';
  structuredDiff?: StructuredDiff[];

  loadWorkflow: (id: string) => Promise<void>;
  saveWorkflow: () => Promise<boolean>;
  deleteWorkflow: (id: string) => Promise<void>;
  newWorkflow: () => void;
  setWorkflowName: (name: string) => void;
  setIsActive: (active: boolean) => void;
  setProjectId: (projectId: string | null) => void;
  bumpCredentialRevision: () => void;
  setDraftSource: (source?: 'Ai' | 'Human') => void;
  setDraftStatus: (status?: 'Pending' | 'Rejected' | 'Confirmed') => void;
  setStructuredDiff: (diff?: StructuredDiff[]) => void;
}

export const useWorkflowStore = create<WorkflowState>((set, get) => ({
  workflowId: null,
  projectId: null,
  workflowName: '',
  workflowVersion: 1,
  isActive: false,
  isDirty: false,
  saving: false,
  credentialRevision: 0,
  draftSource: undefined,
  draftStatus: undefined,
  structuredDiff: undefined,

  setWorkflowName: (name) => set({ workflowName: name, isDirty: true }),
  setIsActive: (active) => set({ isActive: active, isDirty: true }),
  setProjectId: (projectId) => set({ projectId }),

  bumpCredentialRevision: () => set({ credentialRevision: get().credentialRevision + 1 }),

  setDraftSource: (source) => set({ draftSource: source }),
  setDraftStatus: (status) => set({ draftStatus: status }),
  setStructuredDiff: (diff) => set({ structuredDiff: diff }),

  loadWorkflow: async (id) => {
    try {
      const workflow = await api.getWorkflow(id);
      const canvas = useCanvasStore.getState();
      const { nodes, edges } = deserializeWorkflow(workflow, canvas.nodeTypes);
      const styleSettings: WorkflowStyleSettings = workflow.styleSettings
        ? { ...DEFAULT_STYLE_SETTINGS, ...workflow.styleSettings }
        : { ...DEFAULT_STYLE_SETTINGS };

      // 计划约定：载入时若存在位置为 null 的节点（后端未定位），自动布局，
      // 避免这些节点堆叠在原点。已正确定位的节点不会被重排（仅当存在未定位节点时才整体布局）。
      const hasUnpositioned = workflow.nodes.some((n) => n.positionX == null || n.positionY == null);
      const positions = hasUnpositioned ? computeAutoLayout(nodes, edges, styleSettings.layoutDirection) : null;
      const finalNodes = hasUnpositioned
        ? nodes.map((n) => ({ ...n, position: positions?.[n.id] ?? n.position }))
        : nodes;

      canvas.loadFromWorkflow({
        nodes: finalNodes,
        edges,
        styleSettings,
        reviewMode: workflow.source === 'Ai' && workflow.draftStatus === 'Pending',
      });

      set({
        workflowId: workflow.id,
        projectId: workflow.projectId,
        workflowName: workflow.name,
        workflowVersion: workflow.version,
        isActive: workflow.isActive,
        isDirty: false,
        draftSource: workflow.source === 'Ai' ? 'Ai' : 'Human',
        draftStatus: workflow.draftStatus,
        structuredDiff: workflow.diff,
      });
    } catch (err) {
      console.error('Failed to load workflow:', err);
      throw err;
    }
  },

  saveWorkflow: async () => {
    const canvas = useCanvasStore.getState();
    canvas.flushPositions();
    if (!canvas.validateAllNodes()) {
      notifications.show({
        title: i18n.t('common:saveFailed'),
        message: i18n.t('common:saveBlockedByValidation'),
        color: 'red',
        autoClose: 3000,
      });
      return false;
    }

    const { workflowId, workflowName, isActive, projectId } = get();
    const { nodes, edges, styleSettings } = canvas;
    const { nodeDefinitions, connections } = serializeWorkflow(nodes, edges, workflowName);

    set({ saving: true });
    try {
      if (workflowId) {
        const updated = await api.updateWorkflow(workflowId, {
          name: workflowName,
          isActive,
          styleSettings,
          nodes: nodeDefinitions,
          connections,
        });
        set({ workflowVersion: updated.version });
      } else {
        const created = await api.createWorkflow({
          name: workflowName || 'Untitled Workflow',
          createdBy: 'user',
          projectId: projectId ?? undefined,
          nodes: nodeDefinitions,
          connections,
        });
        set({ workflowId: created.id, workflowVersion: created.version });
      }
      set({ isDirty: false });
      useCanvasStore.setState({ validationErrors: {} });
      return true;
    } catch (err) {
      console.error('Failed to save workflow:', err);
      throw err;
    } finally {
      set({ saving: false });
    }
  },

  newWorkflow: () => {
    useCanvasStore.getState().resetCanvasState();
    set({
      workflowId: null,
      projectId: null,
      workflowName: '',
      isActive: false,
      isDirty: false,
      draftSource: undefined,
      draftStatus: undefined,
      structuredDiff: undefined,
    });
  },

  deleteWorkflow: async (id) => {
    try {
      await api.deleteWorkflow(id);
    } catch (err) {
      console.error('Failed to delete workflow:', err);
      throw err;
    }
  },
}));

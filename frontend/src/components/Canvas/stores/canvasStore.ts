import { create } from 'zustand';
import { applyNodeChanges, applyEdgeChanges } from '@xyflow/react';
import type { NodeChange, EdgeChange } from '@xyflow/react';
import type {
  NodeTypeDescriptor,
  ParameterDefinition,
  WorkflowStyleSettings,
  NodeExecutionRecordDto,
  RetryPolicyDto,
} from '../../../types/workflow.ts';
import { DEFAULT_STYLE_SETTINGS } from '../../../types/workflow.ts';
import { validateParameters } from '../../../utils/validateParameters.ts';
import { computeAutoLayout } from '../../../utils/workflowLayout.ts';
import type { WorkflowNode, WorkflowEdge, WorkflowNodeData } from '../../../types/canvas.ts';

interface HistorySnapshot {
  nodes: WorkflowNode[];
  edges: WorkflowEdge[];
}

const MAX_HISTORY = 50;

interface CanvasState {
  /** 画布节点（高频更新） */
  nodes: WorkflowNode[];
  /** position-only cache：拖拽期间只更新此缓存，不触碰 `nodes`，避免整树重渲染 */
  nodePositions: Record<string, { x: number; y: number }>;
  edges: WorkflowEdge[];
  selectedNodeId: string | null;
  /** 可用节点类型（addNode 依赖，随画布渲染，置于画布 store 避免与 workflowStore 循环依赖） */
  nodeTypes: NodeTypeDescriptor[];
  styleSettings: WorkflowStyleSettings;
  /** 字段级校验错误：nodeId → fieldName → message */
  validationErrors: Record<string, Record<string, string>>;
  /** 复制/粘贴剪贴板：被复制节点的 data（含参数与设置） */
  copiedNode: WorkflowNodeData | null;
  isExecuting: boolean;
  /** nodeDefinitionId → NodeExecutionRecordDto，累积存储，不覆盖 */
  nodeExecutionRecords: Record<string, NodeExecutionRecordDto>;
  reviewMode: boolean;
  /** 画布脏标记：画布节点/边/样式等本地变更即为脏（不污染 workflowStore）。 */
  isDirty: boolean;

  setNodes: (nodes: WorkflowNode[]) => void;
  setEdges: (edges: WorkflowEdge[]) => void;
  onNodesChange: (changes: NodeChange<WorkflowNode>[]) => void;
  onEdgesChange: (changes: EdgeChange[]) => void;
  addNode: (typeName: string, position: { x: number; y: number }) => void;
  removeNode: (nodeId: string) => void;
  updateNodePosition: (nodeId: string, position: { x: number; y: number }) => void;
  updateNodeParameters: (nodeId: string, parameters: Record<string, unknown>) => void;
  updateNodeName: (nodeId: string, name: string) => void;
  updateNodeSettings: (
    nodeId: string,
    settings: { errorStrategy?: string; retryPolicy?: RetryPolicyDto | null; timeout?: number | null },
  ) => void;
  copyNode: (nodeId: string) => void;
  pasteNode: (position: { x: number; y: number }) => void;
  addEdge: (source: string, sourceHandle: string | null, target: string, targetHandle: string | null) => void;
  removeEdge: (edgeId: string) => void;
  setSelectedNode: (nodeId: string | null) => void;
  setNodeTypes: (types: NodeTypeDescriptor[]) => void;
  setStyleSettings: (settings: WorkflowStyleSettings) => void;
  setIsExecuting: (executing: boolean) => void;
  updateNodeExecutionStatus: (nodeId: string, status: WorkflowNodeData['executionStatus']) => void;
  clearExecutionStatuses: () => void;
  upsertNodeExecutionRecords: (records: NodeExecutionRecordDto[]) => void;
  clearNodeExecutionRecords: () => void;
  setReviewMode: (mode: boolean) => void;
  validateAllNodes: () => boolean;
  canUndo: boolean;
  canRedo: boolean;
  undo: () => void;
  redo: () => void;
  pushHistory: () => void;
  autoLayout: () => void;

  /** 供 workflowStore 在 loadWorkflow/saveWorkflow/newWorkflow 中编排画布状态 */
  loadFromWorkflow: (payload: {
    nodes: WorkflowNode[];
    edges: WorkflowEdge[];
    styleSettings: WorkflowStyleSettings;
    reviewMode: boolean;
  }) => void;
  resetCanvasState: () => void;
  /** 将拖拽缓存的位置合并回 nodes（保存/撤销前调用） */
  flushPositions: () => void;
}

function buildNodeFromDescriptor(
  descriptor: NodeTypeDescriptor,
  position: { x: number; y: number },
  existingNodes: WorkflowNode[],
): WorkflowNode {
  const id = `${descriptor.typeName}_${Date.now()}_${Math.random().toString(36).slice(2, 8)}`;
  const sameTypeCount = existingNodes.filter((n) => n.data.typeName === descriptor.typeName).length;
  const displayName = descriptor.displayName;
  const name = sameTypeCount > 0 ? `${displayName} ${sameTypeCount + 1}` : displayName;

  const defaultParams: Record<string, unknown> = {};
  for (const p of descriptor.parameters) {
    defaultParams[p.name] = p.defaultValue ?? '';
  }

  return {
    id,
    type: 'workflow',
    position,
    data: {
      typeName: descriptor.typeName,
      name,
      parameters: defaultParams,
      isEntry: descriptor.defaultIsEntry,
      descriptor,
      errorStrategy: 'Terminate',
      retryPolicy: null,
      timeout: null,
    },
  };
}

export const useCanvasStore = create<CanvasState>((set, get) => {
  // 撤销/重做栈保存在闭包中，不进入可序列化 state。
  const undoStack: HistorySnapshot[] = [];
  const redoStack: HistorySnapshot[] = [];

  function flushPositions() {
    const { nodePositions, nodes } = get();
    if (Object.keys(nodePositions).length === 0) return;
    set({
      nodes: nodes.map((n) => {
        const pos = nodePositions[n.id];
        return pos ? { ...n, position: pos } : n;
      }),
      nodePositions: {},
    });
  }

  function markDirty() {
    set({ isDirty: true });
  }

  function snapshot(): HistorySnapshot {
    // 避免 JSON 序列化深拷贝（丢失 Date/undefined 等），改用结构化克隆。
    return {
      nodes: structuredClone(get().nodes),
      edges: structuredClone(get().edges),
    };
  }

  function pushHistoryInternal() {
    flushPositions();
    undoStack.push(snapshot());
    if (undoStack.length > MAX_HISTORY) {
      undoStack.shift();
    }
    redoStack.length = 0;
  }

  function validateNodeParameters(
    parameters: Record<string, unknown>,
    definitions: ParameterDefinition[],
  ): Record<string, string> {
    return validateParameters(parameters, definitions);
  }

  return {
    nodes: [],
    nodePositions: {},
    edges: [],
    selectedNodeId: null,
    nodeTypes: [],
    styleSettings: { ...DEFAULT_STYLE_SETTINGS },
    validationErrors: {},
    copiedNode: null,
    isExecuting: false,
    nodeExecutionRecords: {},
    reviewMode: false,
    isDirty: false,
    canUndo: false,
    canRedo: false,

    setNodes: (nodes) => {
      set({ nodes });
      markDirty();
    },
    setEdges: (edges) => {
      set({ edges });
      markDirty();
    },

    onNodesChange: (changes) => {
      const hasNonPositionChange = changes.some((c) => c.type !== 'position' || c.dragging === false);
      if (hasNonPositionChange) {
        set({ nodes: applyNodeChanges<WorkflowNode>(changes, get().nodes), nodePositions: {} });
        markDirty();
      } else {
        const posUpdates: Record<string, { x: number; y: number }> = {};
        for (const c of changes) {
          if (c.type === 'position' && c.position) {
            posUpdates[c.id] = c.position;
          }
        }
        set({ nodePositions: { ...get().nodePositions, ...posUpdates } });
      }
    },

    onEdgesChange: (changes) => {
      set({ edges: applyEdgeChanges(changes, get().edges) });
      markDirty();
    },

    addNode: (typeName, position) => {
      pushHistoryInternal();
      const descriptor = get().nodeTypes.find((t) => t.typeName === typeName);
      if (!descriptor) return;
      const node = buildNodeFromDescriptor(descriptor, position, get().nodes);
      node.selected = true;
      const deselectedNodes = get().nodes.map((n) => ({ ...n, selected: false }));
      set({ nodes: [...deselectedNodes, node], selectedNodeId: node.id, canUndo: true, canRedo: false });
      markDirty();
    },

    removeNode: (nodeId) => {
      pushHistoryInternal();
      set({
        nodes: get().nodes.filter((n) => n.id !== nodeId),
        edges: get().edges.filter((e) => e.source !== nodeId && e.target !== nodeId),
        selectedNodeId: get().selectedNodeId === nodeId ? null : get().selectedNodeId,
        canUndo: true,
        canRedo: false,
      });
      markDirty();
    },

    updateNodePosition: (nodeId, position) => {
      set({
        nodes: get().nodes.map((n) => (n.id === nodeId ? { ...n, position } : n)),
      });
      markDirty();
    },

    updateNodeParameters: (nodeId, parameters) => {
      set({
        nodes: get().nodes.map((n) =>
          n.id === nodeId ? { ...n, data: { ...n.data, parameters } } : n,
        ),
      });
      markDirty();
    },

    updateNodeName: (nodeId, name) => {
      set({
        nodes: get().nodes.map((n) =>
          n.id === nodeId ? { ...n, data: { ...n.data, name } } : n,
        ),
      });
      markDirty();
    },

    updateNodeSettings: (nodeId, settings) => {
      set({
        nodes: get().nodes.map((n) =>
          n.id === nodeId
            ? { ...n, data: { ...n.data, ...settings } }
            : n,
        ),
      });
      markDirty();
    },

    copyNode: (nodeId) => {
      const node = get().nodes.find((n) => n.id === nodeId);
      if (!node) return;
      set({ copiedNode: structuredClone(node.data) });
    },

    pasteNode: (position) => {
      const src = get().copiedNode;
      if (!src) return;
      pushHistoryInternal();
      const id = `${src.typeName}_${Date.now()}_${Math.random().toString(36).slice(2, 8)}`;
      const newNode: WorkflowNode = {
        id,
        type: 'workflow',
        position,
        selected: true,
        data: {
          ...structuredClone(src),
          name: `${src.name} copy`,
          executionStatus: undefined,
        },
      };
      const deselected = get().nodes.map((n) => ({ ...n, selected: false }));
      set({ nodes: [...deselected, newNode], selectedNodeId: id, canUndo: true, canRedo: false });
      markDirty();
    },

    addEdge: (source, sourceHandle, target, targetHandle) => {
      pushHistoryInternal();
      const id = `e_${source}-${sourceHandle ?? 'out'}-${target}-${targetHandle ?? 'in'}_${Date.now()}`;
      const edge: WorkflowEdge = {
        id,
        source,
        target,
        sourceHandle,
        targetHandle,
        type: 'workflow',
        animated: false,
      };
      set({ edges: [...get().edges, edge], canUndo: true, canRedo: false });
      markDirty();
    },

    removeEdge: (edgeId) => {
      pushHistoryInternal();
      set({ edges: get().edges.filter((e) => e.id !== edgeId), canUndo: true, canRedo: false });
      markDirty();
    },

    setSelectedNode: (nodeId) => set({ selectedNodeId: nodeId }),

    setNodeTypes: (types) => set({ nodeTypes: types }),

    setStyleSettings: (settings) => {
      set({ styleSettings: settings });
      markDirty();
    },

    setIsExecuting: (executing) => {
      set({ isExecuting: executing });
    },

    updateNodeExecutionStatus: (nodeId, status) => {
      set({
        nodes: get().nodes.map((n) =>
          n.id === nodeId
            ? { ...n, data: { ...n.data, executionStatus: status } }
            : n,
        ),
      });
    },

    clearExecutionStatuses: () => {
      set({
        nodes: get().nodes.map((n) => ({
          ...n,
          data: { ...n.data, executionStatus: undefined },
        })),
      });
    },

    upsertNodeExecutionRecords: (records) => {
      const existing = get().nodeExecutionRecords;
      const merged = { ...existing };
      for (const r of records) {
        merged[r.nodeDefinitionId] = r;
      }
      set({ nodeExecutionRecords: merged });
    },

    clearNodeExecutionRecords: () => {
      set({ nodeExecutionRecords: {} });
    },

    setReviewMode: (mode) => set({ reviewMode: mode }),

    validateAllNodes: () => {
      const { nodes } = get();
      const errors: Record<string, Record<string, string>> = {};

      for (const node of nodes) {
        const { descriptor, parameters } = node.data;
        const fieldErrors = validateNodeParameters(parameters, descriptor.parameters);
        if (Object.keys(fieldErrors).length > 0) {
          errors[node.id] = fieldErrors;
        }
      }

      set({ validationErrors: errors });
      return Object.keys(errors).length === 0;
    },

    undo: () => {
      const snap = undoStack.pop();
      if (!snap) return;

      redoStack.push(snapshot());
      set({
        nodes: snap.nodes,
        nodePositions: {},
        edges: snap.edges,
        canUndo: undoStack.length > 0,
        canRedo: true,
      });
      markDirty();
    },

    redo: () => {
      const snap = redoStack.pop();
      if (!snap) return;

      undoStack.push(snapshot());
      set({
        nodes: snap.nodes,
        nodePositions: {},
        edges: snap.edges,
        canUndo: true,
        canRedo: redoStack.length > 0,
      });
      markDirty();
    },

    pushHistory: () => {
      pushHistoryInternal();
      set({ canUndo: true, canRedo: false });
    },

    autoLayout: () => {
      const { nodes, edges, styleSettings } = get();
      const positions = computeAutoLayout(nodes, edges, styleSettings.layoutDirection);
      set({
        nodes: nodes.map((n) => ({ ...n, position: positions[n.id] ?? n.position })),
      });
      markDirty();
    },

    loadFromWorkflow: ({ nodes, edges, styleSettings, reviewMode }) => {
      undoStack.length = 0;
      redoStack.length = 0;
      set({
        styleSettings,
        nodes,
        edges,
        nodePositions: {},
        selectedNodeId: null,
        validationErrors: {},
        isExecuting: false,
        nodeExecutionRecords: {},
        reviewMode,
        isDirty: false,
        canUndo: false,
        canRedo: false,
      });
    },

    resetCanvasState: () => {
      undoStack.length = 0;
      redoStack.length = 0;
      set({
        nodes: [],
        nodePositions: {},
        edges: [],
        selectedNodeId: null,
        styleSettings: { ...DEFAULT_STYLE_SETTINGS },
        validationErrors: {},
        copiedNode: null,
        isExecuting: false,
        nodeExecutionRecords: {},
        reviewMode: false,
        isDirty: false,
        canUndo: false,
        canRedo: false,
      });
    },

    flushPositions: () => {
      flushPositions();
    },
  };
});

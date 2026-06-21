import { create } from 'zustand';
import { applyNodeChanges, applyEdgeChanges } from '@xyflow/react';
import type { Node, Edge, NodeChange, EdgeChange } from '@xyflow/react';
import type { NodeTypeDescriptor, ParameterDefinition, WorkflowStyleSettings, NodeExecutionRecordDto } from '../types/workflow.ts';
import { DEFAULT_STYLE_SETTINGS } from '../types/workflow.ts';
import { deserializeWorkflow, serializeWorkflow } from '../utils/workflowSerializer.ts';
import { validateParameters } from '../utils/validateParameters.ts';
import * as api from '../services/api.ts';

export type WorkflowNodeData = {
  typeName: string;
  name: string;
  parameters: Record<string, unknown>;
  isEntry: boolean;
  descriptor: NodeTypeDescriptor;
  errorStrategy: string;
  retryPolicy: string | null;
  timeout: number | null;
  executionStatus?: 'idle' | 'running' | 'success' | 'error' | 'waiting';
};

export type WorkflowNode = Node<WorkflowNodeData, 'workflow'>;
export type WorkflowEdge = Edge;

interface HistorySnapshot {
  nodes: WorkflowNode[];
  edges: WorkflowEdge[];
}

const MAX_HISTORY = 50;

interface WorkflowState {
  nodes: WorkflowNode[];
  edges: WorkflowEdge[];
  selectedNodeId: string | null;
  nodeTypes: NodeTypeDescriptor[];
  workflowId: string | null;
  workflowName: string;
  isActive: boolean;
  styleSettings: WorkflowStyleSettings;
  isDirty: boolean;
  saving: boolean;
  /** 字段级校验错误：nodeId → fieldName → message */
  validationErrors: Record<string, Record<string, string>>;
  isExecuting: boolean;
  /** nodeDefinitionId → NodeExecutionRecordDto，累积存储，不覆盖 */
  nodeExecutionRecords: Record<string, NodeExecutionRecordDto>;

  setNodes: (nodes: WorkflowNode[]) => void;
  setEdges: (edges: WorkflowEdge[]) => void;
  onNodesChange: (changes: NodeChange<WorkflowNode>[]) => void;
  onEdgesChange: (changes: EdgeChange[]) => void;
  addNode: (typeName: string, position: { x: number; y: number }) => void;
  removeNode: (nodeId: string) => void;
  updateNodePosition: (nodeId: string, position: { x: number; y: number }) => void;
  updateNodeParameters: (nodeId: string, parameters: Record<string, unknown>) => void;
  updateNodeName: (nodeId: string, name: string) => void;
  updateNodeSettings: (nodeId: string, settings: { errorStrategy?: string; retryPolicy?: string | null }) => void;
  addEdge: (source: string, sourceHandle: string | null, target: string, targetHandle: string | null) => void;
  removeEdge: (edgeId: string) => void;
  setSelectedNode: (nodeId: string | null) => void;
  setNodeTypes: (types: NodeTypeDescriptor[]) => void;
  setWorkflowName: (name: string) => void;
  setIsActive: (active: boolean) => void;
  setStyleSettings: (settings: WorkflowStyleSettings) => void;
  loadWorkflow: (id: string) => Promise<void>;
  saveWorkflow: () => Promise<boolean>;
  deleteWorkflow: (id: string) => Promise<void>;
  newWorkflow: () => void;
  validateAllNodes: () => boolean;
  setIsExecuting: (executing: boolean) => void;
  updateNodeExecutionStatus: (nodeId: string, status: WorkflowNodeData['executionStatus']) => void;
  clearExecutionStatuses: () => void;
  upsertNodeExecutionRecords: (records: NodeExecutionRecordDto[]) => void;
  clearNodeExecutionRecords: () => void;
  canUndo: boolean;
  canRedo: boolean;
  undo: () => void;
  redo: () => void;
  pushHistory: () => void;
}

function buildNodeFromDescriptor(
  descriptor: NodeTypeDescriptor,
  position: { x: number; y: number },
  existingNodes: WorkflowNode[],
): WorkflowNode {
  const id = `${descriptor.typeName}_${Date.now()}_${Math.random().toString(36).slice(2, 8)}`;
  const sameTypeCount = existingNodes.filter((n) => n.data.typeName === descriptor.typeName).length;
  const name = sameTypeCount > 0 ? `${descriptor.displayName} ${sameTypeCount + 1}` : descriptor.displayName;

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

export const useWorkflowStore = create<WorkflowState>((set, get) => {
  const undoStack: HistorySnapshot[] = [];
  const redoStack: HistorySnapshot[] = [];

  function snapshot(): HistorySnapshot {
    return {
      nodes: JSON.parse(JSON.stringify(get().nodes)) as WorkflowNode[],
      edges: JSON.parse(JSON.stringify(get().edges)) as WorkflowEdge[],
    };
  }

  function pushHistoryInternal() {
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
    edges: [],
    selectedNodeId: null,
    nodeTypes: [],
    workflowId: null,
    workflowName: '',
    isActive: false,
        styleSettings: { ...DEFAULT_STYLE_SETTINGS },
	    isDirty: false,
	    saving: false,
	    validationErrors: {},
	    isExecuting: false,
	    nodeExecutionRecords: {},
	    canUndo: false,
    canRedo: false,

    setNodes: (nodes) => set({ nodes, isDirty: true }),
    setEdges: (edges) => set({ edges, isDirty: true }),

    onNodesChange: (changes) => {
      set({ nodes: applyNodeChanges<WorkflowNode>(changes, get().nodes), isDirty: true });
    },

    onEdgesChange: (changes) => {
      set({ edges: applyEdgeChanges(changes, get().edges), isDirty: true });
    },

    addNode: (typeName, position) => {
      pushHistoryInternal();
      const descriptor = get().nodeTypes.find((t) => t.typeName === typeName);
      if (!descriptor) return;
      const node = buildNodeFromDescriptor(descriptor, position, get().nodes);
      node.selected = true;
      const deselectedNodes = get().nodes.map((n) => ({ ...n, selected: false }));
      set({ nodes: [...deselectedNodes, node], selectedNodeId: node.id, isDirty: true, canUndo: true, canRedo: false });
    },

    removeNode: (nodeId) => {
      pushHistoryInternal();
      set({
        nodes: get().nodes.filter((n) => n.id !== nodeId),
        edges: get().edges.filter((e) => e.source !== nodeId && e.target !== nodeId),
        selectedNodeId: get().selectedNodeId === nodeId ? null : get().selectedNodeId,
        isDirty: true,
        canUndo: true,
        canRedo: false,
      });
    },

    updateNodePosition: (nodeId, position) => {
      set({
        nodes: get().nodes.map((n) => (n.id === nodeId ? { ...n, position } : n)),
        isDirty: true,
      });
    },

    updateNodeParameters: (nodeId, parameters) => {
      set({
        nodes: get().nodes.map((n) =>
          n.id === nodeId ? { ...n, data: { ...n.data, parameters } } : n,
        ),
        isDirty: true,
      });
    },

    updateNodeName: (nodeId, name) => {
      set({
        nodes: get().nodes.map((n) =>
          n.id === nodeId ? { ...n, data: { ...n.data, name } } : n,
        ),
        isDirty: true,
      });
    },

    updateNodeSettings: (nodeId, settings) => {
      set({
        nodes: get().nodes.map((n) =>
          n.id === nodeId
            ? { ...n, data: { ...n.data, ...settings } }
            : n,
        ),
        isDirty: true,
      });
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
      set({ edges: [...get().edges, edge], isDirty: true, canUndo: true, canRedo: false });
    },

    removeEdge: (edgeId) => {
      pushHistoryInternal();
      set({ edges: get().edges.filter((e) => e.id !== edgeId), isDirty: true, canUndo: true, canRedo: false });
    },

    setSelectedNode: (nodeId) => set({ selectedNodeId: nodeId }),

    setNodeTypes: (types) => set({ nodeTypes: types }),

    setWorkflowName: (name) => set({ workflowName: name, isDirty: true }),

    setIsActive: (active) => set({ isActive: active, isDirty: true }),

    setStyleSettings: (settings) => set({ styleSettings: settings, isDirty: true }),

    loadWorkflow: async (id) => {
      try {
        const workflow = await api.getWorkflow(id);
        const { nodes, edges } = deserializeWorkflow(workflow, get().nodeTypes);
        undoStack.length = 0;
        redoStack.length = 0;
        set({
          workflowId: workflow.id,
          workflowName: workflow.name,
          isActive: workflow.isActive,
          styleSettings: workflow.styleSettings ? { ...DEFAULT_STYLE_SETTINGS, ...workflow.styleSettings } : { ...DEFAULT_STYLE_SETTINGS },
          nodes,
          edges,
          selectedNodeId: null,
          isDirty: false,
          validationErrors: {},
          canUndo: false,
          canRedo: false,
        });
      } catch (err) {
        console.error('Failed to load workflow:', err);
        throw err;
      }
    },

    saveWorkflow: async () => {
      if (!get().validateAllNodes()) return false;

      const { workflowId, workflowName, isActive, styleSettings, nodes, edges } = get();
      const { nodeDefinitions, connections } = serializeWorkflow(nodes, edges, workflowName);

      set({ saving: true });
      try {
        if (workflowId) {
          await api.updateWorkflow(workflowId, {
            name: workflowName,
            isActive,
            styleSettings,
            nodes: nodeDefinitions,
            connections,
          });
        } else {
          const created = await api.createWorkflow({
            name: workflowName || 'Untitled Workflow',
            createdBy: 'user',
            nodes: nodeDefinitions,
            connections,
          });
          set({ workflowId: created.id });
        }
        set({ isDirty: false, validationErrors: {} });
        return true;
      } catch (err) {
        console.error('Failed to save workflow:', err);
        throw err;
      } finally {
        set({ saving: false });
      }
    },

    newWorkflow: () => {
      undoStack.length = 0;
      redoStack.length = 0;
      set({
        workflowId: null,
        workflowName: '',
        isActive: false,
    styleSettings: { ...DEFAULT_STYLE_SETTINGS },
        nodes: [],
        edges: [],
        selectedNodeId: null,
        isDirty: false,
        validationErrors: {},
        canUndo: false,
        canRedo: false,
      });
    },

    deleteWorkflow: async (id: string) => {
      await api.deleteWorkflow(id);
    },

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

    undo: () => {
      const snap = undoStack.pop();
      if (!snap) return;

      redoStack.push(snapshot());
      set({
        nodes: snap.nodes,
        edges: snap.edges,
        isDirty: true,
        canUndo: undoStack.length > 0,
        canRedo: true,
      });
    },

    redo: () => {
      const snap = redoStack.pop();
      if (!snap) return;

      undoStack.push(snapshot());
      set({
        nodes: snap.nodes,
        edges: snap.edges,
        isDirty: true,
        canUndo: true,
        canRedo: redoStack.length > 0,
      });
    },

    pushHistory: () => {
      pushHistoryInternal();
      set({ canUndo: true, canRedo: false });
    },
  };
});

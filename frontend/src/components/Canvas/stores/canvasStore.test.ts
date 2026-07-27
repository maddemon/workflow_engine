import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { useCanvasStore } from './canvasStore.ts';
import { useWorkflowStore } from '../../../stores/workflowStore.ts';
import type { NodeTypeDescriptor, NodeExecutionRecordDto } from '../../../types/workflow.ts';
import type { WorkflowNode } from './canvasStore.ts';

const descriptor: NodeTypeDescriptor = {
  typeName: 'httpRequest',
  displayName: 'HTTP Request',
  category: 'Http',
  categoryKey: 'logic',
  icon: '',
  executionMode: 'Sync',
  parameters: [],
  ports: [
    { name: 'out', displayName: 'Out', direction: 'Output', type: 'Main', required: false },
    { name: 'in', displayName: 'In', direction: 'Input', type: 'Main', required: false },
  ],
  defaultIsEntry: true,
};

function makeNode(id: string, params: Record<string, unknown> = {}): WorkflowNode {
  return {
    id,
    type: 'workflow',
    position: { x: 0, y: 0 },
    data: {
      typeName: descriptor.typeName,
      name: `Node ${id}`,
      parameters: params,
      isEntry: true,
      descriptor,
      errorStrategy: 'Terminate',
      retryPolicy: null,
      timeout: null,
    },
  };
}

function resetStore() {
  useWorkflowStore.getState().newWorkflow();
  useCanvasStore.setState({
    nodeTypes: [descriptor],
    copiedNode: null,
    validationErrors: {},
  });
}

describe('canvasStore', () => {
  beforeEach(() => {
    resetStore();
  });

  afterEach(() => {
    vi.clearAllMocks();
  });

  describe('node operations', () => {
    it('setNodes updates nodes and marks dirty', () => {
      useCanvasStore.getState().setNodes([makeNode('n1')]);
      expect(useCanvasStore.getState().nodes).toHaveLength(1);
      expect(useWorkflowStore.getState().isDirty).toBe(true);
    });

    it('setSelectedNode updates selection', () => {
      useCanvasStore.getState().setSelectedNode('n1');
      expect(useCanvasStore.getState().selectedNodeId).toBe('n1');
    });

    it('onNodesChange applies non-position changes', () => {
      useCanvasStore.getState().setNodes([makeNode('n1')]);
      useCanvasStore.getState().onNodesChange([{ id: 'n1', type: 'select', selected: true }]);
      expect(useCanvasStore.getState().nodes[0].selected).toBe(true);
      expect(useWorkflowStore.getState().isDirty).toBe(true);
    });

    it('onNodesChange with dragging position updates nodePositions', () => {
      useCanvasStore.getState().setNodes([makeNode('n1')]);
      useWorkflowStore.setState({ isDirty: false });
      useCanvasStore.getState().onNodesChange([{ id: 'n1', type: 'position', position: { x: 10, y: 20 }, dragging: true }]);
      expect(useCanvasStore.getState().nodePositions['n1']).toEqual({ x: 10, y: 20 });
      expect(useWorkflowStore.getState().isDirty).toBe(false);
    });

    it('onNodesChange with non-dragging position applies to nodes', () => {
      useCanvasStore.getState().setNodes([makeNode('n1')]);
      useCanvasStore.getState().onNodesChange([{ id: 'n1', type: 'position', position: { x: 10, y: 20 }, dragging: false }]);
      expect(useCanvasStore.getState().nodes[0].position).toEqual({ x: 10, y: 20 });
      expect(useWorkflowStore.getState().isDirty).toBe(true);
    });

    it('addNode creates a node at the given position', () => {
      useCanvasStore.getState().addNode(descriptor.typeName, { x: 100, y: 200 });
      const nodes = useCanvasStore.getState().nodes;
      expect(nodes).toHaveLength(1);
      expect(nodes[0].data.typeName).toBe(descriptor.typeName);
      expect(nodes[0].position).toEqual({ x: 100, y: 200 });
      expect(nodes[0].selected).toBe(true);
      expect(useCanvasStore.getState().selectedNodeId).toBe(nodes[0].id);
    });

    it('addNode with unknown type is a no-op', () => {
      useCanvasStore.getState().addNode('unknown', { x: 0, y: 0 });
      expect(useCanvasStore.getState().nodes).toHaveLength(0);
    });

    it('removeNode removes node and connected edges', () => {
      useCanvasStore.getState().setNodes([makeNode('n1'), makeNode('n2')]);
      useCanvasStore.getState().addEdge('n1', 'out', 'n2', 'in');
      useCanvasStore.getState().setSelectedNode('n1');
      useCanvasStore.getState().removeNode('n1');
      expect(useCanvasStore.getState().nodes).toHaveLength(1);
      expect(useCanvasStore.getState().edges).toHaveLength(0);
      expect(useCanvasStore.getState().selectedNodeId).toBeNull();
    });

    it('updateNodePosition updates node position', () => {
      useCanvasStore.getState().setNodes([makeNode('n1')]);
      useCanvasStore.getState().updateNodePosition('n1', { x: 5, y: 6 });
      expect(useCanvasStore.getState().nodes[0].position).toEqual({ x: 5, y: 6 });
    });

    it('updateNodeParameters updates parameters', () => {
      useCanvasStore.getState().setNodes([makeNode('n1')]);
      useCanvasStore.getState().updateNodeParameters('n1', { url: 'http://example.com' });
      expect(useCanvasStore.getState().nodes[0].data.parameters).toEqual({ url: 'http://example.com' });
    });

    it('updateNodeName updates name', () => {
      useCanvasStore.getState().setNodes([makeNode('n1')]);
      useCanvasStore.getState().updateNodeName('n1', 'Renamed');
      expect(useCanvasStore.getState().nodes[0].data.name).toBe('Renamed');
    });

    it('updateNodeSettings supports timeout without dropping other settings', () => {
      useCanvasStore.getState().setNodes([makeNode('n1')]);

      useCanvasStore.getState().updateNodeSettings('n1', { timeout: 30 });
      const afterTimeout = useCanvasStore.getState().nodes.find((n) => n.id === 'n1')!;
      expect(afterTimeout.data.timeout).toBe(30);

      useCanvasStore.getState().updateNodeSettings('n1', { errorStrategy: 'Continue', timeout: null });
      const afterClear = useCanvasStore.getState().nodes.find((n) => n.id === 'n1')!;
      expect(afterClear.data.timeout).toBeNull();
      expect(afterClear.data.errorStrategy).toBe('Continue');
    });

    it('copyNode + pasteNode duplicates the node with a renamed copy at the given position', () => {
      useCanvasStore.getState().setNodes([makeNode('n1')]);

      useCanvasStore.getState().copyNode('n1');
      expect(useCanvasStore.getState().copiedNode).not.toBeNull();

      useCanvasStore.getState().pasteNode({ x: 100, y: 100 });

      const nodes = useCanvasStore.getState().nodes;
      expect(nodes.length).toBe(2);
      const copy = nodes.find((n) => n.id !== 'n1')!;
      expect(copy.data.name).toBe('Node n1 copy');
      expect(copy.position).toEqual({ x: 100, y: 100 });
      expect(copy.data.timeout).toBeNull();
      expect(useCanvasStore.getState().selectedNodeId).toBe(copy.id);
    });

    it('copyNode with unknown id is a no-op', () => {
      useCanvasStore.getState().setNodes([makeNode('n1')]);
      useCanvasStore.getState().copyNode('missing');
      expect(useCanvasStore.getState().copiedNode).toBeNull();
    });
  });

  describe('edge operations', () => {
    it('addEdge creates an edge between nodes', () => {
      useCanvasStore.getState().setNodes([makeNode('n1'), makeNode('n2')]);
      useCanvasStore.getState().addEdge('n1', 'out', 'n2', 'in');
      const edges = useCanvasStore.getState().edges;
      expect(edges).toHaveLength(1);
      expect(edges[0].source).toBe('n1');
      expect(edges[0].target).toBe('n2');
      expect(edges[0].sourceHandle).toBe('out');
      expect(edges[0].targetHandle).toBe('in');
    });

    it('removeEdge removes edge by id', () => {
      useCanvasStore.getState().setNodes([makeNode('n1'), makeNode('n2')]);
      useCanvasStore.getState().addEdge('n1', 'out', 'n2', 'in');
      const edgeId = useCanvasStore.getState().edges[0].id;
      useCanvasStore.getState().removeEdge(edgeId);
      expect(useCanvasStore.getState().edges).toHaveLength(0);
    });

    it('onEdgesChange applies edge changes', () => {
      useCanvasStore.getState().setEdges([{ id: 'e1', source: 'n1', target: 'n2' }]);
      useCanvasStore.getState().onEdgesChange([{ id: 'e1', type: 'select', selected: true }]);
      expect(useCanvasStore.getState().edges[0].selected).toBe(true);
    });
  });

  describe('canvas settings', () => {
    it('setStyleSettings updates settings and marks dirty', () => {
      useCanvasStore.getState().setStyleSettings({ layoutDirection: 'vertical' });
      expect(useCanvasStore.getState().styleSettings.layoutDirection).toBe('vertical');
      expect(useWorkflowStore.getState().isDirty).toBe(true);
    });

    it('setNodeTypes updates available types', () => {
      useCanvasStore.getState().setNodeTypes([descriptor]);
      expect(useCanvasStore.getState().nodeTypes).toEqual([descriptor]);
    });

    it('setReviewMode toggles review mode', () => {
      useCanvasStore.getState().setReviewMode(true);
      expect(useCanvasStore.getState().reviewMode).toBe(true);
    });
  });

  describe('execution state', () => {
    it('setIsExecuting updates executing flag', () => {
      useCanvasStore.getState().setIsExecuting(true);
      expect(useCanvasStore.getState().isExecuting).toBe(true);
    });

    it('updateNodeExecutionStatus updates status', () => {
      useCanvasStore.getState().setNodes([makeNode('n1')]);
      useCanvasStore.getState().updateNodeExecutionStatus('n1', 'running');
      expect(useCanvasStore.getState().nodes[0].data.executionStatus).toBe('running');
    });

    it('clearExecutionStatuses clears all statuses', () => {
      useCanvasStore.getState().setNodes([makeNode('n1'), makeNode('n2')]);
      useCanvasStore.getState().updateNodeExecutionStatus('n1', 'success');
      useCanvasStore.getState().updateNodeExecutionStatus('n2', 'error');
      useCanvasStore.getState().clearExecutionStatuses();
      expect(useCanvasStore.getState().nodes[0].data.executionStatus).toBeUndefined();
      expect(useCanvasStore.getState().nodes[1].data.executionStatus).toBeUndefined();
    });

    it('upsertNodeExecutionRecords merges records', () => {
      useCanvasStore.getState().upsertNodeExecutionRecords([
        { nodeDefinitionId: 'n1', status: 'Completed' } as unknown as NodeExecutionRecordDto,
      ]);
      expect(useCanvasStore.getState().nodeExecutionRecords['n1']).toBeDefined();
    });

    it('clearNodeExecutionRecords clears records', () => {
      useCanvasStore.getState().upsertNodeExecutionRecords([
        { nodeDefinitionId: 'n1', status: 'Completed' } as unknown as NodeExecutionRecordDto,
      ]);
      useCanvasStore.getState().clearNodeExecutionRecords();
      expect(Object.keys(useCanvasStore.getState().nodeExecutionRecords)).toHaveLength(0);
    });
  });

  describe('validation', () => {
    it('validateAllNodes returns true when no errors', () => {
      useCanvasStore.getState().setNodes([makeNode('n1')]);
      const result = useCanvasStore.getState().validateAllNodes();
      expect(result).toBe(true);
      expect(Object.keys(useCanvasStore.getState().validationErrors)).toHaveLength(0);
    });

    it('validateAllNodes returns false and records errors for invalid params', () => {
      const paramDef = { name: 'url', displayName: 'URL', type: 'String', required: true, defaultValue: '', validationRules: [], displayRule: null, credentialType: null, options: [] } as unknown as NodeTypeDescriptor['parameters'][0];
      const desc = { ...descriptor, parameters: [paramDef] };
      useCanvasStore.setState({ nodeTypes: [desc] });
      useCanvasStore.getState().setNodes([{
        ...makeNode('n1'),
        data: { ...makeNode('n1').data, descriptor: desc, parameters: {} },
      }]);
      const result = useCanvasStore.getState().validateAllNodes();
      expect(result).toBe(false);
      expect(useCanvasStore.getState().validationErrors['n1']['url']).toBeDefined();
    });
  });

  describe('history', () => {
    it('undo and redo restore previous node state', () => {
      useCanvasStore.getState().setNodes([makeNode('n1')]);
      useCanvasStore.getState().pushHistory();
      useCanvasStore.getState().setNodes([makeNode('n1'), makeNode('n2')]);

      useCanvasStore.getState().pushHistory();
      useCanvasStore.getState().setNodes([makeNode('n1'), makeNode('n2'), makeNode('n3')]);

      expect(useCanvasStore.getState().canUndo).toBe(true);
      useCanvasStore.getState().undo();
      expect(useCanvasStore.getState().nodes).toHaveLength(2);
      expect(useCanvasStore.getState().canRedo).toBe(true);

      useCanvasStore.getState().redo();
      expect(useCanvasStore.getState().nodes).toHaveLength(3);
    });

    it('pushHistory marks undo available', () => {
      useCanvasStore.getState().setNodes([makeNode('n1')]);
      useCanvasStore.getState().pushHistory();
      expect(useCanvasStore.getState().canUndo).toBe(true);
      expect(useCanvasStore.getState().canRedo).toBe(false);
    });
  });

  describe('auto layout', () => {
    it('autoLayout updates node positions', () => {
      useCanvasStore.getState().setNodes([makeNode('n1'), makeNode('n2')]);
      useCanvasStore.getState().addEdge('n1', 'out', 'n2', 'in');
      useCanvasStore.getState().autoLayout();
      const positions = useCanvasStore.getState().nodes.map((n) => n.position);
      expect(positions[0]).not.toEqual(positions[1]);
      expect(useWorkflowStore.getState().isDirty).toBe(true);
    });
  });

  describe('resetCanvasState', () => {
    it('resets all canvas state', () => {
      useCanvasStore.getState().setNodes([makeNode('n1')]);
      useCanvasStore.getState().setStyleSettings({ layoutDirection: 'vertical' });
      useCanvasStore.getState().resetCanvasState();
      expect(useCanvasStore.getState().nodes).toHaveLength(0);
      // 重置回到默认样式（DEFAULT_STYLE_SETTINGS.layoutDirection = 'horizontal'）
      expect(useCanvasStore.getState().styleSettings.layoutDirection).toBe('horizontal');
    });
  });
});

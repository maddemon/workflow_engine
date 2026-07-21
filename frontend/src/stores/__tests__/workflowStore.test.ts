import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { useWorkflowStore } from '../workflowStore.ts';
import type { NodeTypeDescriptor, NodeExecutionRecordDto, Workflow } from '../../types/workflow.ts';
import type { WorkflowNode } from '../workflowStore.ts';
import * as api from '../../services/api.ts';
import * as serializer from '../../utils/workflowSerializer.ts';

const mockedApi = vi.mocked(api);
const mockedSerializer = vi.mocked(serializer);

vi.mock('../../services/api.ts', () => ({
  getWorkflow: vi.fn(),
  updateWorkflow: vi.fn(),
  createWorkflow: vi.fn(),
  deleteWorkflow: vi.fn(),
}));

vi.mock('../../utils/workflowSerializer.ts', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../utils/workflowSerializer.ts')>();
  return {
    ...actual,
    serializeWorkflow: vi.fn(actual.serializeWorkflow),
    deserializeWorkflow: vi.fn(actual.deserializeWorkflow),
  };
});

const descriptor: NodeTypeDescriptor = {
  typeName: 'httpRequest',
  displayName: 'HTTP Request',
  category: 'Http',
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
  useWorkflowStore.setState({
    nodeTypes: [descriptor],
    copiedNode: null,
    credentialRevision: 0,
    validationErrors: {},
  });
}

describe('workflowStore', () => {
  beforeEach(() => {
    resetStore();
  });

  afterEach(() => {
    vi.clearAllMocks();
  });

  describe('node operations', () => {
    it('setNodes updates nodes and marks dirty', () => {
      useWorkflowStore.getState().setNodes([makeNode('n1')]);
      expect(useWorkflowStore.getState().nodes).toHaveLength(1);
      expect(useWorkflowStore.getState().isDirty).toBe(true);
    });

    it('setSelectedNode updates selection', () => {
      useWorkflowStore.getState().setSelectedNode('n1');
      expect(useWorkflowStore.getState().selectedNodeId).toBe('n1');
    });

    it('onNodesChange applies non-position changes', () => {
      useWorkflowStore.getState().setNodes([makeNode('n1')]);
      useWorkflowStore.getState().onNodesChange([{ id: 'n1', type: 'select', selected: true }]);
      expect(useWorkflowStore.getState().nodes[0].selected).toBe(true);
      expect(useWorkflowStore.getState().isDirty).toBe(true);
    });

    it('onNodesChange with dragging position updates nodePositions', () => {
      useWorkflowStore.getState().setNodes([makeNode('n1')]);
      useWorkflowStore.setState({ isDirty: false });
      useWorkflowStore.getState().onNodesChange([{ id: 'n1', type: 'position', position: { x: 10, y: 20 }, dragging: true }]);
      expect(useWorkflowStore.getState().nodePositions['n1']).toEqual({ x: 10, y: 20 });
      expect(useWorkflowStore.getState().isDirty).toBe(false);
    });

    it('onNodesChange with non-dragging position applies to nodes', () => {
      useWorkflowStore.getState().setNodes([makeNode('n1')]);
      useWorkflowStore.getState().onNodesChange([{ id: 'n1', type: 'position', position: { x: 10, y: 20 }, dragging: false }]);
      expect(useWorkflowStore.getState().nodes[0].position).toEqual({ x: 10, y: 20 });
      expect(useWorkflowStore.getState().isDirty).toBe(true);
    });

    it('addNode creates a node at the given position', () => {
      useWorkflowStore.getState().addNode(descriptor.typeName, { x: 100, y: 200 });
      const nodes = useWorkflowStore.getState().nodes;
      expect(nodes).toHaveLength(1);
      expect(nodes[0].data.typeName).toBe(descriptor.typeName);
      expect(nodes[0].position).toEqual({ x: 100, y: 200 });
      expect(nodes[0].selected).toBe(true);
      expect(useWorkflowStore.getState().selectedNodeId).toBe(nodes[0].id);
    });

    it('addNode with unknown type is a no-op', () => {
      useWorkflowStore.getState().addNode('unknown', { x: 0, y: 0 });
      expect(useWorkflowStore.getState().nodes).toHaveLength(0);
    });

    it('removeNode removes node and connected edges', () => {
      useWorkflowStore.getState().setNodes([makeNode('n1'), makeNode('n2')]);
      useWorkflowStore.getState().addEdge('n1', 'out', 'n2', 'in');
      useWorkflowStore.getState().setSelectedNode('n1');
      useWorkflowStore.getState().removeNode('n1');
      expect(useWorkflowStore.getState().nodes).toHaveLength(1);
      expect(useWorkflowStore.getState().edges).toHaveLength(0);
      expect(useWorkflowStore.getState().selectedNodeId).toBeNull();
    });

    it('updateNodePosition updates node position', () => {
      useWorkflowStore.getState().setNodes([makeNode('n1')]);
      useWorkflowStore.getState().updateNodePosition('n1', { x: 5, y: 6 });
      expect(useWorkflowStore.getState().nodes[0].position).toEqual({ x: 5, y: 6 });
    });

    it('updateNodeParameters updates parameters', () => {
      useWorkflowStore.getState().setNodes([makeNode('n1')]);
      useWorkflowStore.getState().updateNodeParameters('n1', { url: 'http://example.com' });
      expect(useWorkflowStore.getState().nodes[0].data.parameters).toEqual({ url: 'http://example.com' });
    });

    it('updateNodeName updates name', () => {
      useWorkflowStore.getState().setNodes([makeNode('n1')]);
      useWorkflowStore.getState().updateNodeName('n1', 'Renamed');
      expect(useWorkflowStore.getState().nodes[0].data.name).toBe('Renamed');
    });

    it('updateNodeSettings supports timeout without dropping other settings', () => {
      useWorkflowStore.getState().setNodes([makeNode('n1')]);

      useWorkflowStore.getState().updateNodeSettings('n1', { timeout: 30 });
      const afterTimeout = useWorkflowStore.getState().nodes.find((n) => n.id === 'n1')!;
      expect(afterTimeout.data.timeout).toBe(30);

      useWorkflowStore.getState().updateNodeSettings('n1', { errorStrategy: 'Continue', timeout: null });
      const afterClear = useWorkflowStore.getState().nodes.find((n) => n.id === 'n1')!;
      expect(afterClear.data.timeout).toBeNull();
      expect(afterClear.data.errorStrategy).toBe('Continue');
    });

    it('copyNode + pasteNode duplicates the node with a renamed copy at the given position', () => {
      useWorkflowStore.getState().setNodes([makeNode('n1')]);

      useWorkflowStore.getState().copyNode('n1');
      expect(useWorkflowStore.getState().copiedNode).not.toBeNull();

      useWorkflowStore.getState().pasteNode({ x: 100, y: 100 });

      const nodes = useWorkflowStore.getState().nodes;
      expect(nodes.length).toBe(2);
      const copy = nodes.find((n) => n.id !== 'n1')!;
      expect(copy.data.name).toBe('Node n1 copy');
      expect(copy.position).toEqual({ x: 100, y: 100 });
      expect(copy.data.timeout).toBeNull();
      expect(useWorkflowStore.getState().selectedNodeId).toBe(copy.id);
    });

    it('copyNode with unknown id is a no-op', () => {
      useWorkflowStore.getState().setNodes([makeNode('n1')]);
      useWorkflowStore.getState().copyNode('missing');
      expect(useWorkflowStore.getState().copiedNode).toBeNull();
    });

    it('bumpCredentialRevision increments the revision counter', () => {
      const before = useWorkflowStore.getState().credentialRevision;
      useWorkflowStore.getState().bumpCredentialRevision();
      expect(useWorkflowStore.getState().credentialRevision).toBe(before + 1);
    });
  });

  describe('edge operations', () => {
    it('addEdge creates an edge between nodes', () => {
      useWorkflowStore.getState().setNodes([makeNode('n1'), makeNode('n2')]);
      useWorkflowStore.getState().addEdge('n1', 'out', 'n2', 'in');
      const edges = useWorkflowStore.getState().edges;
      expect(edges).toHaveLength(1);
      expect(edges[0].source).toBe('n1');
      expect(edges[0].target).toBe('n2');
      expect(edges[0].sourceHandle).toBe('out');
      expect(edges[0].targetHandle).toBe('in');
    });

    it('removeEdge removes edge by id', () => {
      useWorkflowStore.getState().setNodes([makeNode('n1'), makeNode('n2')]);
      useWorkflowStore.getState().addEdge('n1', 'out', 'n2', 'in');
      const edgeId = useWorkflowStore.getState().edges[0].id;
      useWorkflowStore.getState().removeEdge(edgeId);
      expect(useWorkflowStore.getState().edges).toHaveLength(0);
    });

    it('onEdgesChange applies edge changes', () => {
      useWorkflowStore.getState().setEdges([{ id: 'e1', source: 'n1', target: 'n2' }]);
      useWorkflowStore.getState().onEdgesChange([{ id: 'e1', type: 'select', selected: true }]);
      expect(useWorkflowStore.getState().edges[0].selected).toBe(true);
    });
  });

  describe('workflow metadata', () => {
    it('setWorkflowName updates name and marks dirty', () => {
      useWorkflowStore.getState().setWorkflowName('New Name');
      expect(useWorkflowStore.getState().workflowName).toBe('New Name');
      expect(useWorkflowStore.getState().isDirty).toBe(true);
    });

    it('setIsActive updates active and marks dirty', () => {
      useWorkflowStore.getState().setIsActive(true);
      expect(useWorkflowStore.getState().isActive).toBe(true);
      expect(useWorkflowStore.getState().isDirty).toBe(true);
    });

    it('setStyleSettings updates settings and marks dirty', () => {
      useWorkflowStore.getState().setStyleSettings({ layoutDirection: 'vertical' });
      expect(useWorkflowStore.getState().styleSettings.layoutDirection).toBe('vertical');
      expect(useWorkflowStore.getState().isDirty).toBe(true);
    });

    it('setProjectId updates project id', () => {
      useWorkflowStore.getState().setProjectId('p1');
      expect(useWorkflowStore.getState().projectId).toBe('p1');
    });

    it('setNodeTypes updates available types', () => {
      useWorkflowStore.getState().setNodeTypes([descriptor]);
      expect(useWorkflowStore.getState().nodeTypes).toEqual([descriptor]);
    });
  });

  describe('review and draft state', () => {
    it('setReviewMode toggles review mode', () => {
      useWorkflowStore.getState().setReviewMode(true);
      expect(useWorkflowStore.getState().reviewMode).toBe(true);
    });

    it('setDraftSource and setDraftStatus update state', () => {
      useWorkflowStore.getState().setDraftSource('Ai');
      useWorkflowStore.getState().setDraftStatus('Pending');
      expect(useWorkflowStore.getState().draftSource).toBe('Ai');
      expect(useWorkflowStore.getState().draftStatus).toBe('Pending');
    });

    it('setStructuredDiff updates diff', () => {
      const diff = [{ op: 'add' }] as unknown as NonNullable<ReturnType<typeof useWorkflowStore.getState>['structuredDiff']>;
      useWorkflowStore.getState().setStructuredDiff(diff);
      expect(useWorkflowStore.getState().structuredDiff).toEqual(diff);
    });
  });

  describe('execution state', () => {
    it('setIsExecuting updates executing flag', () => {
      useWorkflowStore.getState().setIsExecuting(true);
      expect(useWorkflowStore.getState().isExecuting).toBe(true);
    });

    it('updateNodeExecutionStatus updates status', () => {
      useWorkflowStore.getState().setNodes([makeNode('n1')]);
      useWorkflowStore.getState().updateNodeExecutionStatus('n1', 'running');
      expect(useWorkflowStore.getState().nodes[0].data.executionStatus).toBe('running');
    });

    it('clearExecutionStatuses clears all statuses', () => {
      useWorkflowStore.getState().setNodes([makeNode('n1'), makeNode('n2')]);
      useWorkflowStore.getState().updateNodeExecutionStatus('n1', 'success');
      useWorkflowStore.getState().updateNodeExecutionStatus('n2', 'error');
      useWorkflowStore.getState().clearExecutionStatuses();
      expect(useWorkflowStore.getState().nodes[0].data.executionStatus).toBeUndefined();
      expect(useWorkflowStore.getState().nodes[1].data.executionStatus).toBeUndefined();
    });

    it('upsertNodeExecutionRecords merges records', () => {
      useWorkflowStore.getState().upsertNodeExecutionRecords([
        { nodeDefinitionId: 'n1', status: 'Completed' } as unknown as NodeExecutionRecordDto,
      ]);
      expect(useWorkflowStore.getState().nodeExecutionRecords['n1']).toBeDefined();
    });

    it('clearNodeExecutionRecords clears records', () => {
      useWorkflowStore.getState().upsertNodeExecutionRecords([
        { nodeDefinitionId: 'n1', status: 'Completed' } as unknown as NodeExecutionRecordDto,
      ]);
      useWorkflowStore.getState().clearNodeExecutionRecords();
      expect(Object.keys(useWorkflowStore.getState().nodeExecutionRecords)).toHaveLength(0);
    });
  });

  describe('validation', () => {
    it('validateAllNodes returns true when no errors', () => {
      useWorkflowStore.getState().setNodes([makeNode('n1')]);
      const result = useWorkflowStore.getState().validateAllNodes();
      expect(result).toBe(true);
      expect(Object.keys(useWorkflowStore.getState().validationErrors)).toHaveLength(0);
    });

    it('validateAllNodes returns false and records errors for invalid params', () => {
      const paramDef = { name: 'url', displayName: 'URL', type: 'String', required: true, defaultValue: '', validationRules: [], displayRule: null, credentialType: null, options: [] } as unknown as NodeTypeDescriptor['parameters'][0];
      const desc = { ...descriptor, parameters: [paramDef] };
      useWorkflowStore.setState({ nodeTypes: [desc] });
      useWorkflowStore.getState().setNodes([{
        ...makeNode('n1'),
        data: { ...makeNode('n1').data, descriptor: desc, parameters: {} },
      }]);
      const result = useWorkflowStore.getState().validateAllNodes();
      expect(result).toBe(false);
      expect(useWorkflowStore.getState().validationErrors['n1']['url']).toBeDefined();
    });
  });

  describe('history', () => {
    it('undo and redo restore previous node state', () => {
      useWorkflowStore.getState().setNodes([makeNode('n1')]);
      useWorkflowStore.getState().pushHistory();
      useWorkflowStore.getState().setNodes([makeNode('n1'), makeNode('n2')]);

      useWorkflowStore.getState().pushHistory();
      useWorkflowStore.getState().setNodes([makeNode('n1'), makeNode('n2'), makeNode('n3')]);

      expect(useWorkflowStore.getState().canUndo).toBe(true);
      useWorkflowStore.getState().undo();
      expect(useWorkflowStore.getState().nodes).toHaveLength(2);
      expect(useWorkflowStore.getState().canRedo).toBe(true);

      useWorkflowStore.getState().redo();
      expect(useWorkflowStore.getState().nodes).toHaveLength(3);
    });

    it('pushHistory marks undo available', () => {
      useWorkflowStore.getState().setNodes([makeNode('n1')]);
      useWorkflowStore.getState().pushHistory();
      expect(useWorkflowStore.getState().canUndo).toBe(true);
      expect(useWorkflowStore.getState().canRedo).toBe(false);
    });
  });

  describe('auto layout', () => {
    it('autoLayout updates node positions', () => {
      useWorkflowStore.getState().setNodes([makeNode('n1'), makeNode('n2')]);
      useWorkflowStore.getState().addEdge('n1', 'out', 'n2', 'in');
      useWorkflowStore.getState().autoLayout();
      const positions = useWorkflowStore.getState().nodes.map((n) => n.position);
      expect(positions[0]).not.toEqual(positions[1]);
      expect(useWorkflowStore.getState().isDirty).toBe(true);
    });
  });

  describe('newWorkflow', () => {
    it('resets all workflow state', () => {
      useWorkflowStore.getState().setNodes([makeNode('n1')]);
      useWorkflowStore.getState().setWorkflowName('Test');
      useWorkflowStore.getState().setIsActive(true);
      useWorkflowStore.getState().newWorkflow();
      expect(useWorkflowStore.getState().nodes).toHaveLength(0);
      expect(useWorkflowStore.getState().workflowName).toBe('');
      expect(useWorkflowStore.getState().isActive).toBe(false);
      expect(useWorkflowStore.getState().isDirty).toBe(false);
    });
  });

  describe('loadWorkflow', () => {
    it('loads workflow state from api', async () => {
      const workflow: Workflow = {
        id: 'w1',
        projectId: 'p1',
        name: 'Test Workflow',
        version: 2,
        createdBy: 'u',
        createdAt: '',
        updatedAt: '',
        isActive: true,
        styleSettings: { layoutDirection: 'vertical' },
        nodes: [],
        connections: [],
      };
      mockedApi.getWorkflow.mockResolvedValue(workflow);
      mockedSerializer.deserializeWorkflow.mockReturnValue({ nodes: [makeNode('n1')], edges: [] });

      await useWorkflowStore.getState().loadWorkflow('w1');

      expect(useWorkflowStore.getState().workflowId).toBe('w1');
      expect(useWorkflowStore.getState().projectId).toBe('p1');
      expect(useWorkflowStore.getState().workflowName).toBe('Test Workflow');
      expect(useWorkflowStore.getState().workflowVersion).toBe(2);
      expect(useWorkflowStore.getState().isActive).toBe(true);
      expect(useWorkflowStore.getState().styleSettings.layoutDirection).toBe('vertical');
      expect(useWorkflowStore.getState().nodes).toHaveLength(1);
      expect(useWorkflowStore.getState().isDirty).toBe(false);
    });

    it('rethrows load errors', async () => {
      mockedApi.getWorkflow.mockRejectedValue(new Error('network error'));
      await expect(useWorkflowStore.getState().loadWorkflow('w1')).rejects.toThrow('network error');
    });
  });

  describe('saveWorkflow', () => {
    it('returns false when validation fails', async () => {
      const paramDef = { name: 'url', displayName: 'URL', type: 'String', required: true, defaultValue: '', validationRules: [], displayRule: null, credentialType: null, options: [] } as unknown as NodeTypeDescriptor['parameters'][0];
      const desc = { ...descriptor, parameters: [paramDef] };
      useWorkflowStore.setState({ nodeTypes: [desc] });
      useWorkflowStore.getState().setNodes([{
        ...makeNode('n1'),
        data: { ...makeNode('n1').data, descriptor: desc, parameters: {} },
      }]);
      const result = await useWorkflowStore.getState().saveWorkflow();
      expect(result).toBe(false);
      expect(mockedApi.updateWorkflow).not.toHaveBeenCalled();
    });

    it('creates workflow when workflowId is null', async () => {
      useWorkflowStore.getState().setWorkflowName('New');
      useWorkflowStore.getState().setProjectId('p1');
      useWorkflowStore.getState().setNodes([makeNode('n1')]);
      mockedSerializer.serializeWorkflow.mockReturnValue({ nodeDefinitions: [], connections: [] });
      mockedApi.createWorkflow.mockResolvedValue({ id: 'w1' } as unknown as Workflow);

      const result = await useWorkflowStore.getState().saveWorkflow();

      expect(result).toBe(true);
      expect(mockedApi.createWorkflow).toHaveBeenCalled();
      expect(useWorkflowStore.getState().workflowId).toBe('w1');
      expect(useWorkflowStore.getState().isDirty).toBe(false);
    });

    it('updates workflow when workflowId exists', async () => {
      useWorkflowStore.setState({ workflowId: 'w1' });
      useWorkflowStore.getState().setWorkflowName('Updated');
      useWorkflowStore.getState().setNodes([makeNode('n1')]);
      mockedSerializer.serializeWorkflow.mockReturnValue({ nodeDefinitions: [], connections: [] });
      mockedApi.updateWorkflow.mockResolvedValue({ id: 'w1' } as unknown as Workflow);

      const result = await useWorkflowStore.getState().saveWorkflow();

      expect(result).toBe(true);
      expect(mockedApi.updateWorkflow).toHaveBeenCalled();
      expect(useWorkflowStore.getState().isDirty).toBe(false);
    });

    it('rethrows save errors', async () => {
      useWorkflowStore.getState().setNodes([makeNode('n1')]);
      mockedSerializer.serializeWorkflow.mockReturnValue({ nodeDefinitions: [], connections: [] });
      mockedApi.updateWorkflow.mockRejectedValue(new Error('save failed'));
      useWorkflowStore.setState({ workflowId: 'w1' });
      await expect(useWorkflowStore.getState().saveWorkflow()).rejects.toThrow('save failed');
      expect(useWorkflowStore.getState().saving).toBe(false);
    });
  });

  describe('deleteWorkflow', () => {
    it('calls api deleteWorkflow', async () => {
      mockedApi.deleteWorkflow.mockResolvedValue(undefined);
      await useWorkflowStore.getState().deleteWorkflow('w1');
      expect(mockedApi.deleteWorkflow).toHaveBeenCalledWith('w1');
    });

    it('rethrows delete errors', async () => {
      mockedApi.deleteWorkflow.mockRejectedValue(new Error('delete failed'));
      await expect(useWorkflowStore.getState().deleteWorkflow('w1')).rejects.toThrow('delete failed');
    });
  });
});

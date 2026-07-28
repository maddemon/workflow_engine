import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { useWorkflowStore } from '../workflowStore.ts';
import { useCanvasStore } from '../../components/Canvas/stores/canvasStore.ts';
import type { NodeTypeDescriptor, Workflow } from '../../types/workflow.ts';
import type { WorkflowNode } from '../../types/canvas.ts';
import * as api from '../../services/api.ts';
import * as serializer from '../../utils/workflowSerializer.ts';
import { notifications } from '@mantine/notifications';

const mockedApi = vi.mocked(api);
const mockedSerializer = vi.mocked(serializer);
const mockedNotifications = vi.mocked(notifications);

vi.mock('../../services/api.ts', () => ({
  getWorkflow: vi.fn(),
  updateWorkflow: vi.fn(),
  createWorkflow: vi.fn(),
  deleteWorkflow: vi.fn(),
}));

vi.mock('@mantine/notifications', () => ({
  notifications: { show: vi.fn() },
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

describe('workflowStore', () => {
  beforeEach(() => {
    resetStore();
  });

  afterEach(() => {
    vi.clearAllMocks();
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

    it('setProjectId updates project id', () => {
      useWorkflowStore.getState().setProjectId('p1');
      expect(useWorkflowStore.getState().projectId).toBe('p1');
    });
  });

  describe('review and draft state', () => {
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

  describe('loadWorkflow', () => {
    it('loads workflow state from api and orchestrates canvas store', async () => {
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
      expect(useWorkflowStore.getState().isDirty).toBe(false);

      expect(useCanvasStore.getState().styleSettings.layoutDirection).toBe('vertical');
      expect(useCanvasStore.getState().nodes).toHaveLength(1);
      expect(useCanvasStore.getState().reviewMode).toBe(false);
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
      useCanvasStore.setState({ nodeTypes: [desc] });
      useCanvasStore.getState().setNodes([{
        ...makeNode('n1'),
        data: { ...makeNode('n1').data, descriptor: desc, parameters: {} },
      }]);
      const result = await useWorkflowStore.getState().saveWorkflow();
      expect(result).toBe(false);
      expect(mockedApi.updateWorkflow).not.toHaveBeenCalled();
    });

    it('saveWorkflow - validation fails - shows error notification and does not call api', async () => {
      const paramDef = { name: 'url', displayName: 'URL', type: 'String', required: true, defaultValue: '', validationRules: [], displayRule: null, credentialType: null, options: [] } as unknown as NodeTypeDescriptor['parameters'][0];
      const desc = { ...descriptor, parameters: [paramDef] };
      useCanvasStore.setState({ nodeTypes: [desc] });
      useCanvasStore.getState().setNodes([{
        ...makeNode('n1'),
        data: { ...makeNode('n1').data, descriptor: desc, parameters: {} },
      }]);

      const result = await useWorkflowStore.getState().saveWorkflow();

      expect(result).toBe(false);
      expect(mockedNotifications.show).toHaveBeenCalled();
      const shown = mockedNotifications.show.mock.calls[0]?.[0] as { color?: string } | undefined;
      expect(shown?.color).toBe('red');
      expect(mockedApi.updateWorkflow).not.toHaveBeenCalled();
      expect(mockedApi.createWorkflow).not.toHaveBeenCalled();
    });

    it('creates workflow when workflowId is null', async () => {
      useWorkflowStore.getState().setWorkflowName('New');
      useWorkflowStore.getState().setProjectId('p1');
      useCanvasStore.getState().setNodes([makeNode('n1')]);
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
      useCanvasStore.getState().setNodes([makeNode('n1')]);
      mockedSerializer.serializeWorkflow.mockReturnValue({ nodeDefinitions: [], connections: [] });
      mockedApi.updateWorkflow.mockResolvedValue({ id: 'w1', version: 5 } as unknown as Workflow);

      const result = await useWorkflowStore.getState().saveWorkflow();

      expect(result).toBe(true);
      expect(mockedApi.updateWorkflow).toHaveBeenCalled();
      expect(useWorkflowStore.getState().isDirty).toBe(false);
    });

    it('updates workflowVersion after update save', async () => {
      useWorkflowStore.setState({ workflowId: 'w1', workflowVersion: 1 });
      useWorkflowStore.getState().setWorkflowName('Updated');
      useCanvasStore.getState().setNodes([makeNode('n1')]);
      mockedSerializer.serializeWorkflow.mockReturnValue({ nodeDefinitions: [], connections: [] });
      mockedApi.updateWorkflow.mockResolvedValue({ id: 'w1', version: 5 } as unknown as Workflow);

      const result = await useWorkflowStore.getState().saveWorkflow();

      expect(result).toBe(true);
      expect(mockedApi.updateWorkflow).toHaveBeenCalled();
      expect(useWorkflowStore.getState().workflowVersion).toBe(5);
      expect(useWorkflowStore.getState().isDirty).toBe(false);
    });

    it('updates workflowId and workflowVersion after create save', async () => {
      useWorkflowStore.setState({ workflowId: null, workflowVersion: 1 });
      useWorkflowStore.getState().setWorkflowName('New');
      useCanvasStore.getState().setNodes([makeNode('n1')]);
      mockedSerializer.serializeWorkflow.mockReturnValue({ nodeDefinitions: [], connections: [] });
      mockedApi.createWorkflow.mockResolvedValue({ id: 'w2', version: 3 } as unknown as Workflow);

      const result = await useWorkflowStore.getState().saveWorkflow();

      expect(result).toBe(true);
      expect(mockedApi.createWorkflow).toHaveBeenCalled();
      expect(useWorkflowStore.getState().workflowId).toBe('w2');
      expect(useWorkflowStore.getState().workflowVersion).toBe(3);
      expect(useWorkflowStore.getState().isDirty).toBe(false);
    });

    it('rethrows save errors', async () => {
      useCanvasStore.getState().setNodes([makeNode('n1')]);
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

  describe('newWorkflow', () => {
    it('resets all workflow and canvas state', () => {
      useCanvasStore.getState().setNodes([makeNode('n1')]);
      useWorkflowStore.getState().setWorkflowName('Test');
      useWorkflowStore.getState().setIsActive(true);
      useWorkflowStore.getState().newWorkflow();
      expect(useWorkflowStore.getState().workflowName).toBe('');
      expect(useWorkflowStore.getState().isActive).toBe(false);
      expect(useWorkflowStore.getState().isDirty).toBe(false);
      expect(useCanvasStore.getState().nodes).toHaveLength(0);
    });
  });
});

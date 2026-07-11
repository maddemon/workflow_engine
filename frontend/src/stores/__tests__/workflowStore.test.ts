import { beforeEach, describe, expect, it } from 'vitest';
import { useWorkflowStore } from '../workflowStore.ts';
import type { NodeTypeDescriptor } from '../../types/workflow.ts';
import type { WorkflowNode } from '../workflowStore.ts';

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

function makeNode(id: string): WorkflowNode {
  return {
    id,
    type: 'workflow',
    position: { x: 0, y: 0 },
    data: {
      typeName: descriptor.typeName,
      name: `Node ${id}`,
      parameters: {},
      isEntry: true,
      descriptor,
      errorStrategy: 'Terminate',
      retryPolicy: null,
      timeout: null,
    },
  };
}

describe('workflowStore node operations', () => {
  beforeEach(() => {
    useWorkflowStore.getState().setNodes([]);
    useWorkflowStore.getState().setSelectedNode(null);
    useWorkflowStore.setState({ copiedNode: null, credentialRevision: 0 });
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

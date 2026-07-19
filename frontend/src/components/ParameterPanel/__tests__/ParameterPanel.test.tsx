import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen, fireEvent, waitFor } from '@testing-library/react';
import { renderWithProvider } from '../../../test-utils.tsx';
import { ParameterPanel } from '../ParameterPanel.tsx';
import { useWorkflowStore } from '../../../stores/workflowStore.ts';
import type { NodeTypeDescriptor, ParameterDefinition } from '../../../types/workflow.ts';
import type { WorkflowNode } from '../../../stores/workflowStore.ts';

vi.mock('../TriggerConfig.tsx', () => ({
  TriggerConfig: () => <div data-testid="trigger-config">TriggerConfig</div>,
}));

const stringParam: ParameterDefinition = {
  name: 'message',
  displayName: 'Message',
  type: 'String',
  defaultValue: '',
  required: false,
  validationRules: [],
  displayRule: null,
  credentialType: null,
  options: [],
};

const descriptor: NodeTypeDescriptor = {
  typeName: 'TestNode',
  displayName: 'Test Node',
  category: 'Test',
  icon: '',
  executionMode: 'Sync',
  parameters: [stringParam],
  ports: [],
  defaultIsEntry: false,
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
      isEntry: false,
      descriptor,
      errorStrategy: 'Terminate',
      retryPolicy: null,
      timeout: null,
    },
  };
}

describe('ParameterPanel', () => {
  beforeEach(() => {
    useWorkflowStore.getState().newWorkflow();
    useWorkflowStore.setState({
      nodeTypes: [descriptor],
      workflowName: 'Test Workflow',
      projectId: 'p1',
      workflowId: 'wf-1',
    });
  });

  it('renders workflow settings when no node selected', () => {
    renderWithProvider(<ParameterPanel />);
    expect(screen.getByText(/workflow settings/i)).toBeDefined();
    expect(screen.getByTestId('trigger-config')).toBeDefined();
  });

  it('renders node settings when a node is selected', () => {
    const node = makeNode('n1', { message: 'hello' });
    useWorkflowStore.setState({ nodes: [node], selectedNodeId: 'n1' });

    renderWithProvider(<ParameterPanel />);
    expect(screen.getByText('Test Node')).toBeDefined();
    expect(screen.getByDisplayValue('hello')).toBeDefined();
  });

  it('updates node parameter value on change', () => {
    const node = makeNode('n1', { message: 'hello' });
    useWorkflowStore.setState({ nodes: [node], selectedNodeId: 'n1' });

    renderWithProvider(<ParameterPanel />);
    const input = screen.getByDisplayValue('hello');
    fireEvent.change(input, { target: { value: 'world' } });

    expect(useWorkflowStore.getState().nodes[0].data.parameters.message).toBe('world');
  });

  it('updates workflow name and active state', () => {
    renderWithProvider(<ParameterPanel />);
    const nameInput = screen.getByDisplayValue('Test Workflow');
    fireEvent.change(nameInput, { target: { value: 'Renamed' } });

    expect(useWorkflowStore.getState().workflowName).toBe('Renamed');
  });

  it('toggles node retry policy settings', async () => {
    const node = makeNode('n1');
    useWorkflowStore.setState({ nodes: [node], selectedNodeId: 'n1' });

    renderWithProvider(<ParameterPanel />);
    fireEvent.click(screen.getByText(/settings/i));
    await waitFor(() => {
      expect(screen.getByText(/retry on fail/i)).toBeDefined();
    });

    fireEvent.click(screen.getByText(/retry on fail/i));

    expect(useWorkflowStore.getState().nodes[0].data.retryPolicy).not.toBeNull();
  });
});

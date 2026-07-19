import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen, fireEvent, waitFor } from '@testing-library/react';
import { ReactFlowProvider } from '@xyflow/react';
import { renderWithProvider } from '../../../test-utils.tsx';
import { WorkflowCanvas } from '../WorkflowCanvas.tsx';
import { useWorkflowStore } from '../../../stores/workflowStore.ts';
import type { NodeTypeDescriptor, PortDefinition } from '../../../types/workflow.ts';
import type { WorkflowNode } from '../../../stores/workflowStore.ts';

vi.mock('@mantine/notifications', () => ({
  notifications: { show: vi.fn() },
}));

import { notifications } from '@mantine/notifications';
const mockedNotifications = vi.mocked(notifications);

const basePorts: PortDefinition[] = [
  { name: 'in', displayName: 'In', direction: 'Input', type: 'Main', required: false },
  { name: 'out', displayName: 'Out', direction: 'Output', type: 'Main', required: false },
];

const descriptor: NodeTypeDescriptor = {
  typeName: 'httpRequest',
  displayName: 'HTTP Request',
  category: 'Http',
  icon: '',
  executionMode: 'Sync',
  parameters: [],
  ports: basePorts,
  defaultIsEntry: true,
};

function makeNode(id: string, portOverrides?: PortDefinition[], overrides: Partial<WorkflowNode['data']> = {}): WorkflowNode {
  return {
    id,
    type: 'workflow',
    position: { x: 0, y: 0 },
    data: {
      typeName: descriptor.typeName,
      name: `Node ${id}`,
      parameters: {},
      isEntry: true,
      descriptor: { ...descriptor, ports: portOverrides ?? basePorts },
      errorStrategy: 'Terminate',
      retryPolicy: null,
      timeout: null,
      ...overrides,
    },
  };
}

function renderCanvas(props = {}) {
  return renderWithProvider(
    <ReactFlowProvider>
      <WorkflowCanvas
        onExecute={vi.fn()}
        onCancel={vi.fn()}
        onDryRun={vi.fn()}
        dryRunLoading={false}
        {...props}
      />
    </ReactFlowProvider>,
  );
}

describe('WorkflowCanvas', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    useWorkflowStore.getState().newWorkflow();
    useWorkflowStore.setState({
      nodeTypes: [descriptor],
    });
  });

  it('renders_canvasToolbar_andReactFlow', async () => {
    renderCanvas();
    expect(await screen.findByText('Execute')).toBeInTheDocument();
    expect(document.querySelector('.workflow-canvas')).toBeInTheDocument();
  });

  it('toolbar_buttons_trigger_callbacks', async () => {
    const onDryRun = vi.fn();
    const onCancel = vi.fn();
    const onExecute = vi.fn();
    useWorkflowStore.setState({
      workflowId: 'wf-1',
      nodes: [makeNode('n1')],
    });

    renderCanvas({ onExecute, onCancel, onDryRun, dryRunLoading: false });
    fireEvent.click(await screen.findByText('Dry Run'));
    expect(onDryRun).toHaveBeenCalled();

    fireEvent.click(screen.getByLabelText('Zoom In'));
    fireEvent.click(screen.getByLabelText('Zoom Out'));
    fireEvent.click(screen.getByLabelText('Fit View'));
  });

  it('selects_node_onClick', async () => {
    useWorkflowStore.setState({
      nodes: [makeNode('n1')],
    });

    renderCanvas();
    const node = await screen.findByText('Node n1');
    fireEvent.click(node);

    await waitFor(() => {
      expect(useWorkflowStore.getState().selectedNodeId).toBe('n1');
    });
  });

  it('deselects_node_onPaneClick', async () => {
    useWorkflowStore.setState({
      nodes: [makeNode('n1')],
      selectedNodeId: 'n1',
    });

    renderCanvas();
    const pane = document.querySelector('.react-flow__pane');
    expect(pane).not.toBeNull();
    fireEvent.click(pane!);

    await waitFor(() => {
      expect(useWorkflowStore.getState().selectedNodeId).toBeNull();
    });
  });

  it('does_not_addNode_onDrop_when_reviewMode', async () => {
    useWorkflowStore.setState({ reviewMode: true });
    renderCanvas();

    const canvas = document.querySelector('.workflow-canvas');
    fireEvent.dragOver(canvas!);
    const dropEvent = new Event('drop', { bubbles: true }) as unknown as DragEvent;
    Object.defineProperty(dropEvent, 'dataTransfer', {
      value: { getData: () => 'httpRequest', dropEffect: 'move' },
    });
    Object.defineProperty(dropEvent, 'preventDefault', { value: vi.fn() });
    fireEvent(canvas!, dropEvent);

    expect(useWorkflowStore.getState().nodes).toHaveLength(0);
  });

  it('does_not_addNode_onDrop_when_executing', async () => {
    useWorkflowStore.setState({ isExecuting: true });
    renderCanvas();

    const canvas = document.querySelector('.workflow-canvas');
    fireEvent.dragOver(canvas!);
    const dropEvent = new Event('drop', { bubbles: true }) as unknown as DragEvent;
    Object.defineProperty(dropEvent, 'dataTransfer', {
      value: { getData: () => 'httpRequest', dropEffect: 'move' },
    });
    Object.defineProperty(dropEvent, 'preventDefault', { value: vi.fn() });
    fireEvent(canvas!, dropEvent);

    expect(useWorkflowStore.getState().nodes).toHaveLength(0);
  });

  it('copies_and_pastes_selectedNode_via_keyboard', async () => {
    useWorkflowStore.setState({
      nodes: [makeNode('n1')],
      selectedNodeId: 'n1',
    });

    renderCanvas();

    fireEvent.keyDown(window, { key: 'c', ctrlKey: true });
    expect(useWorkflowStore.getState().copiedNode).not.toBeNull();

    fireEvent.keyDown(window, { key: 'v', ctrlKey: true });
    await waitFor(() => {
      expect(useWorkflowStore.getState().nodes.length).toBe(2);
    });
    expect(mockedNotifications.show).toHaveBeenCalled();
  });

});

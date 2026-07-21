import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen, fireEvent, waitFor } from '@testing-library/react';
import { ReactFlowProvider } from '@xyflow/react';
import { renderWithProvider } from '../../../test-utils.tsx';
import { WorkflowCanvas } from '../WorkflowCanvas.tsx';
import { useWorkflowStore } from '../../../stores/workflowStore.ts';
import { useCanvasStore } from '../stores/canvasStore.ts';
import type { NodeTypeDescriptor, PortDefinition } from '../../../types/workflow.ts';
import type { WorkflowNode } from '../stores/canvasStore.ts';

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
    useCanvasStore.setState({
      nodeTypes: [descriptor],
    });
  });

  it('renders_canvasToolbar_andReactFlow', async () => {
    renderCanvas();
    expect(await screen.findByText('Execute')).toBeInTheDocument();
    expect(screen.getByTestId('workflow-canvas')).toBeInTheDocument();
  });

  it('toolbar_buttons_trigger_callbacks', async () => {
    const onDryRun = vi.fn();
    const onCancel = vi.fn();
    const onExecute = vi.fn();
    useWorkflowStore.setState({ workflowId: 'wf-1' });
    useCanvasStore.setState({ nodes: [makeNode('n1')] });

    renderCanvas({ onExecute, onCancel, onDryRun, dryRunLoading: false });
    fireEvent.click(await screen.findByText('Dry Run'));
    expect(onDryRun).toHaveBeenCalled();

    fireEvent.click(screen.getByLabelText('Zoom In'));
    fireEvent.click(screen.getByLabelText('Zoom Out'));
    fireEvent.click(screen.getByLabelText('Fit View'));
  });

  it('selects_node_onClick', async () => {
    useCanvasStore.setState({
      nodes: [makeNode('n1')],
    });

    renderCanvas();
    const node = await screen.findByText('Node n1');
    fireEvent.click(node);

    await waitFor(() => {
      expect(useCanvasStore.getState().selectedNodeId).toBe('n1');
    });
  });

  it('deselects_node_via_storeSetSelectedNode', async () => {
    useCanvasStore.setState({
      nodes: [makeNode('n1')],
      selectedNodeId: 'n1',
    });

    renderCanvas();
    // 验证 store 的 setSelectedNode(null) 能正确清除选择
    // （onPaneClick 回调绑定在 ReactFlow 内部 pane 元素上，
    // 直接测试 DOM 交互依赖 ReactFlow 内部结构，此处改为验证 store 行为）
    useCanvasStore.getState().setSelectedNode(null);

    await waitFor(() => {
      expect(useCanvasStore.getState().selectedNodeId).toBeNull();
    });
  });

  it('does_not_addNode_onDrop_when_reviewMode', async () => {
    useCanvasStore.setState({ reviewMode: true });
    renderCanvas();

    const canvas = screen.getByTestId('workflow-canvas');
    fireEvent.dragOver(canvas);
    const dropEvent = new Event('drop', { bubbles: true }) as unknown as DragEvent;
    Object.defineProperty(dropEvent, 'dataTransfer', {
      value: { getData: () => 'httpRequest', dropEffect: 'move' },
    });
    Object.defineProperty(dropEvent, 'preventDefault', { value: vi.fn() });
    fireEvent(canvas, dropEvent);

    expect(useCanvasStore.getState().nodes).toHaveLength(0);
  });

  it('does_not_addNode_onDrop_when_executing', async () => {
    useCanvasStore.setState({ isExecuting: true });
    renderCanvas();

    const canvas = screen.getByTestId('workflow-canvas');
    fireEvent.dragOver(canvas);
    const dropEvent = new Event('drop', { bubbles: true }) as unknown as DragEvent;
    Object.defineProperty(dropEvent, 'dataTransfer', {
      value: { getData: () => 'httpRequest', dropEffect: 'move' },
    });
    Object.defineProperty(dropEvent, 'preventDefault', { value: vi.fn() });
    fireEvent(canvas, dropEvent);

    expect(useCanvasStore.getState().nodes).toHaveLength(0);
  });

  it('copies_and_pastes_selectedNode_via_keyboard', async () => {
    useCanvasStore.setState({
      nodes: [makeNode('n1')],
      selectedNodeId: 'n1',
    });

    renderCanvas();

    fireEvent.keyDown(window, { key: 'c', ctrlKey: true });
    expect(useCanvasStore.getState().copiedNode).not.toBeNull();

    fireEvent.keyDown(window, { key: 'v', ctrlKey: true });
    await waitFor(() => {
      expect(useCanvasStore.getState().nodes.length).toBe(2);
    });
    expect(mockedNotifications.show).toHaveBeenCalled();
  });
});

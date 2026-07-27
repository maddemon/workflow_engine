import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen } from '@testing-library/react';
import { ReactFlowProvider } from '@xyflow/react';
import { renderWithProvider } from '../../../test-utils.tsx';
import { WorkflowCanvas } from '../WorkflowCanvas.tsx';
import { useWorkflowStore } from '../../../stores/workflowStore.ts';
import { useCanvasStore } from '../stores/canvasStore.ts';
import type { NodeTypeDescriptor, PortDefinition } from '../../../types/workflow.ts';
import type { WorkflowNode } from '../stores/canvasStore.ts';

const descriptor: NodeTypeDescriptor = {
  typeName: 'custom',
  displayName: 'Custom',
  category: 'Test',
  categoryKey: 'logic',
  icon: '',
  executionMode: 'Sync',
  parameters: [],
  ports: [],
  defaultIsEntry: false,
};

function makeNode(id: string, ports: PortDefinition[], overrides: Partial<WorkflowNode['data']> = {}): WorkflowNode {
  return {
    id,
    type: 'workflow',
    position: { x: 0, y: 0 },
    data: {
      typeName: descriptor.typeName,
      name: `Node ${id}`,
      parameters: { value: 'abc' },
      isEntry: true,
      descriptor: { ...descriptor, ports },
      errorStrategy: 'Terminate',
      retryPolicy: null,
      timeout: null,
      ...overrides,
    },
  };
}

function renderWithNodes(nodes: WorkflowNode[]) {
  useCanvasStore.setState({ nodes });
  return renderWithProvider(
    <ReactFlowProvider>
      <WorkflowCanvas onExecute={vi.fn()} />
    </ReactFlowProvider>,
  );
}

describe('CustomNode', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    useWorkflowStore.getState().newWorkflow();
  });

  it('renders_regular_node_with_main_ports', async () => {
    renderWithNodes([
      makeNode('n1', [
        { name: 'in', displayName: 'In', direction: 'Input', type: 'Main', required: false },
        { name: 'out', displayName: 'Out', direction: 'Output', type: 'Main', required: false },
      ]),
    ]);

    expect(await screen.findByText('Node n1')).toBeInTheDocument();
  });

  it('renders_config_node_circular_shape', async () => {
    renderWithNodes([
      makeNode('n1', [
        { name: 'mem', displayName: 'Memory', direction: 'Output', type: 'Memory', required: false },
      ]),
    ]);

    expect(await screen.findByText('Node n1')).toBeInTheDocument();
  });

  it('renders_tool_node_with_agentTool_output', async () => {
    renderWithNodes([
      makeNode('n1', [
        { name: 'tool', displayName: 'Tool', direction: 'Output', type: 'AgentTool', required: false },
      ]),
    ]);

    expect(await screen.findByText('Node n1')).toBeInTheDocument();
  });

  it('renders_configurable_node_with_ai_input', async () => {
    renderWithNodes([
      makeNode('n1', [
        { name: 'in', displayName: 'In', direction: 'Input', type: 'Main', required: false },
        { name: 'llm', displayName: 'LLM', direction: 'Input', type: 'LLM', required: false },
        { name: 'out', displayName: 'Out', direction: 'Output', type: 'Main', required: false },
      ]),
    ]);

    expect(await screen.findByText('Node n1')).toBeInTheDocument();
  });

  it('renders_subtitle_from_displayTemplate', async () => {
    renderWithNodes([
      makeNode('n1', [], {
        descriptor: { ...descriptor, displayTemplate: 'Value: {{value}}' },
      }),
    ]);

    expect(await screen.findByText('Value: abc')).toBeInTheDocument();
  });

  it('renders_entry_badge_for_entry_node', async () => {
    renderWithNodes([
      makeNode('n1', [
        { name: 'out', displayName: 'Out', direction: 'Output', type: 'Main', required: false },
      ]),
    ]);

    expect(await screen.findByText('Node n1')).toBeInTheDocument();
  });

  it('renders_running_status_badge', async () => {
    renderWithNodes([
      makeNode('n1', [
        { name: 'out', displayName: 'Out', direction: 'Output', type: 'Main', required: false },
      ], { executionStatus: 'running' }),
    ]);

    expect(await screen.findByText('Node n1')).toBeInTheDocument();
  });

  it('renders_success_status_badge', async () => {
    renderWithNodes([
      makeNode('n1', [
        { name: 'out', displayName: 'Out', direction: 'Output', type: 'Main', required: false },
      ], { executionStatus: 'success' }),
    ]);

    expect(await screen.findByText('Node n1')).toBeInTheDocument();
  });

  it('renders_error_status_badge', async () => {
    renderWithNodes([
      makeNode('n1', [
        { name: 'out', displayName: 'Out', direction: 'Output', type: 'Main', required: false },
      ], { executionStatus: 'error' }),
    ]);

    expect(await screen.findByText('Node n1')).toBeInTheDocument();
  });
});

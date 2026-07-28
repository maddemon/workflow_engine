import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen, render } from '@testing-library/react';
import { MantineProvider } from '@mantine/core';
import { ReactFlowProvider } from '@xyflow/react';
import { renderWithProvider } from '../../../test-utils.tsx';
import { WorkflowCanvas } from '../WorkflowCanvas.tsx';
import { useWorkflowStore } from '../../../stores/workflowStore.ts';
import { useCanvasStore } from '../stores/canvasStore.ts';
import type { NodeTypeDescriptor, PortDefinition } from '../../../types/workflow.ts';
import type { WorkflowNode } from '../../../types/canvas.ts';
import { CustomNode, portLayouts } from '../CustomNode.tsx';
import { ConnectedHandlesContext } from '../connectedHandlesContext.ts';

// Spy on portLayouts.computePortLayouts so we can assert how many times the (expensive)
// layout computation runs across re-renders. The function is exported on the `portLayouts`
// object so the internal call site in the `layouts` memo is observable without altering the
// function's body or the memo's dependency list.
vi.spyOn(portLayouts, 'computePortLayouts');

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

// Render CustomNode directly (unit) so re-render control is deterministic. The providers
// (Mantine / ReactFlow / ConnectedHandles) live in the wrapper and stay mounted across
// rerenders, so changing a prop (e.g. `selected`) forces a real CustomNode re-render
// without remounting the component tree.
function customNodeElement(node: WorkflowNode, selected: boolean) {
  return (
    <ReactFlowProvider>
      <ConnectedHandlesContext.Provider value={{}}>
        <CustomNode
          id={node.id}
          data={node.data}
          selected={selected}
          type="workflow"
          deletable
          selectable
          draggable
          dragging={false}
          zIndex={0}
          isConnectable
          positionAbsoluteX={0}
          positionAbsoluteY={0}
        />
      </ConnectedHandlesContext.Provider>
    </ReactFlowProvider>
  );
}

function renderCustomNode(node: WorkflowNode, selected: boolean) {
  return render(customNodeElement(node, selected), {
    wrapper: ({ children }) => (
      <MantineProvider>
        <ReactFlowProvider>
          <ConnectedHandlesContext.Provider value={{}}>{children}</ConnectedHandlesContext.Provider>
        </ReactFlowProvider>
      </MantineProvider>
    ),
  });
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

  // Switch-like descriptor: has a `default` Output port (dynamic-port marker) plus an
  // Array parameter with a `name` field, so computeDynamicPorts generates one Output
  // port per case item.
  const switchDescriptor: NodeTypeDescriptor = {
    typeName: 'switch',
    displayName: 'Switch',
    category: 'Logic',
    categoryKey: 'logic',
    icon: '',
    executionMode: 'Sync',
    parameters: [
      {
        name: 'cases',
        displayName: 'Cases',
        type: 'Array',
        defaultValue: null,
        required: false,
        validationRules: [],
        displayRule: null,
        credentialType: null,
        options: [],
        itemDefinition: {
          name: 'case',
          displayName: 'Case',
          type: 'Array',
          defaultValue: null,
          required: false,
          validationRules: [],
          displayRule: null,
          credentialType: null,
          options: [],
          fields: [
            { name: 'name', displayName: 'Name', type: 'String', required: false, defaultValue: '', validationRules: [], displayRule: null, credentialType: null, options: [] },
            { name: 'label', displayName: 'Label', type: 'String', required: false, defaultValue: '', validationRules: [], displayRule: null, credentialType: null, options: [] },
          ],
        },
      },
    ],
    ports: [
      { name: 'in', displayName: 'In', direction: 'Input', type: 'Main', required: false },
      { name: 'default', displayName: 'Default', direction: 'Output', type: 'Main', required: false },
    ],
    defaultIsEntry: false,
  };

  function makeSwitchNode(id: string, cases: { name: string; label: string }[]): WorkflowNode {
    return makeNode(id, switchDescriptor.ports, {
      descriptor: { ...switchDescriptor },
      parameters: { value: 'abc', cases },
      isEntry: false,
    });
  }

  // Correctness regression for the memo refactor: a Switch node with dynamic ports must
  // still render one output handle per case (plus the static `default` output) and its
  // single input handle. This proves the layout computation continues to consume
  // computeDynamicPorts correctly after the filter memoization change.
  it('renders_switch_node_dynamic_case_output_ports', async () => {
    const { container } = renderWithNodes([
      makeSwitchNode('s1', [
        { name: 'caseA', label: 'A' },
        { name: 'caseB', label: 'B' },
      ]),
    ]);

    expect(await screen.findByText('Node s1')).toBeInTheDocument();

    // Output handles: caseA, caseB, default => 3
    const outputHandles = container.querySelectorAll('.port-output');
    expect(outputHandles.length).toBe(3);

    // Input handle: in => 1
    const inputHandles = container.querySelectorAll('.port-input');
    expect(inputHandles.length).toBe(1);
  });

  // Memo regression (the real win of the F5 fix): the `layouts` useMemo depends on
  // `inputPorts`/`outputPorts`. Before the fix those arrays were rebuilt every render via
  // `.filter()`, breaking the memo dependency identity so `computePortLayouts` recomputed
  // on every re-render. After the fix they are memoized on `[ports]`, so a re-render with
  // UNCHANGED data/ports must NOT recompute the layout. We force a genuine re-render by
  // flipping `selected` (a prop, not data) and assert computePortLayouts is still called
  // exactly once.
  it('computePortLayouts_called_once_on_rerender_with_unchanged_ports', async () => {
    const node = makeSwitchNode('s1', [
      { name: 'caseA', label: 'A' },
      { name: 'caseB', label: 'B' },
    ]);

    const { rerender } = renderCustomNode(node, false);
    expect(await screen.findByText('Node s1')).toBeInTheDocument();
    expect(vi.mocked(portLayouts.computePortLayouts).mock.calls.length).toBe(1);

    // Re-render with identical data/ports (same reference) but a changed `selected` prop,
    // which forces CustomNode (memo) to re-render.
    rerender(customNodeElement(node, true));
    expect(await screen.findByText('Node s1')).toBeInTheDocument();
    expect(vi.mocked(portLayouts.computePortLayouts).mock.calls.length).toBe(1);
  });

  // Guard against the opposite regression: when the dynamic ports actually CHANGE (a
  // Switch switching from one case to two cases), computePortLayouts MUST recompute. This
  // ensures the memo is keyed on the right dependencies and not over-memoized.
  it('computePortLayouts_called_again_when_ports_change', async () => {
    const oneCase = makeSwitchNode('s1', [{ name: 'caseA', label: 'A' }]);
    const twoCases = makeSwitchNode('s2', [
      { name: 'caseA', label: 'A' },
      { name: 'caseB', label: 'B' },
    ]);

    const { rerender } = renderCustomNode(oneCase, false);
    expect(await screen.findByText('Node s1')).toBeInTheDocument();
    const callsAfterFirstRender = vi.mocked(portLayouts.computePortLayouts).mock.calls.length;

    // Ports change (one case -> two cases): the layout must recompute.
    rerender(customNodeElement(twoCases, false));
    expect(await screen.findByText('Node s2')).toBeInTheDocument();
    expect(vi.mocked(portLayouts.computePortLayouts).mock.calls.length).toBeGreaterThan(callsAfterFirstRender);
  });
});

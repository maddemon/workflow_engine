import type { Edge } from '@xyflow/react';
import type {
  Workflow,
  NodeDefinition,
  Connection,
  NodeTypeDescriptor,
  PortDefinition,
} from '../types/workflow.ts';
import type { WorkflowNodeData, WorkflowNode } from '../stores/workflowStore.ts';

export function serializeWorkflow(
  nodes: WorkflowNode[],
  edges: Edge[],
  _name: string,
): { nodeDefinitions: NodeDefinition[]; connections: Connection[] } {
  const nodeDefinitions: NodeDefinition[] = nodes.map((node) => {
    const data = node.data as WorkflowNodeData;
    return {
      id: node.id,
      typeName: data.typeName,
      name: data.name,
      parameters: data.parameters,
      ports: data.descriptor.ports,
      positionX: Math.round(node.position.x),
      positionY: Math.round(node.position.y),
      isEntry: data.isEntry,
      disabled: false,
      errorStrategy: data.errorStrategy,
      retryPolicy: data.retryPolicy,
      timeout: data.timeout,
    };
  });

  const connections: Connection[] = edges.map((edge) => ({
    id: edge.id,
    sourceNodeId: edge.source,
    sourcePortName: (edge.sourceHandle ?? 'out').replace(/^port-/, ''),
    targetNodeId: edge.target,
    targetPortName: (edge.targetHandle ?? 'in').replace(/^port-/, ''),
  }));

  return { nodeDefinitions, connections };
}

export function deserializeWorkflow(
  workflow: Workflow,
  availableTypes: NodeTypeDescriptor[],
): { nodes: WorkflowNode[]; edges: Edge[] } {
  const inputNodeIds = new Set(workflow.connections.map((c) => c.targetNodeId));

  const nodes: WorkflowNode[] = workflow.nodes.map((ni) => {
    const descriptor = availableTypes.find((t) => t.typeName === ni.typeName) ?? fallbackDescriptor(ni);
    const isExplicitEntry = ni.isEntry || descriptor.defaultIsEntry;
    const isImplicitEntry = !inputNodeIds.has(ni.id);
    const isEntry = isExplicitEntry || isImplicitEntry;

    return {
      id: ni.id,
      type: 'workflow' as const,
      position: { x: ni.positionX, y: ni.positionY },
      data: {
        typeName: ni.typeName,
        name: ni.name,
        parameters: ni.parameters ?? {},
        isEntry,
        descriptor,
        errorStrategy: ni.errorStrategy ?? 'Terminate',
        retryPolicy: ni.retryPolicy,
        timeout: ni.timeout,
      },
    };
  });

  const edges: Edge[] = workflow.connections.map((conn) => ({
    id: conn.id,
    source: conn.sourceNodeId,
    target: conn.targetNodeId,
    sourceHandle: `port-${conn.sourcePortName}`,
    targetHandle: `port-${conn.targetPortName}`,
    type: 'workflow',
    animated: false,
  }));

  return { nodes, edges };
}

function fallbackDescriptor(ni: NodeDefinition): NodeTypeDescriptor {
  const inputPorts: PortDefinition[] = (ni.ports ?? [])
    .filter((p) => p.direction === 'Input')
    .map((p) => ({ ...p }));
  const outputPorts: PortDefinition[] = (ni.ports ?? [])
    .filter((p) => p.direction === 'Output')
    .map((p) => ({ ...p }));

  return {
    typeName: ni.typeName,
    displayName: ni.name || ni.typeName,
    category: 'Unknown',
    icon: '',
    executionMode: 'Sync',
    parameters: [],
    ports: [...inputPorts, ...outputPorts],
    defaultIsEntry: ni.isEntry,
  };
}

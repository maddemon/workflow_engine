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

  // 构建节点端口索引，用于保存时清理无效连接
  const nodePortMap = new Map<string, Set<string>>();
  for (const node of nodes) {
    const ports = new Set<string>();
    for (const port of node.data.descriptor.ports) {
      ports.add(port.name);
    }
    nodePortMap.set(node.id, ports);
  }

  const connections: Connection[] = edges
    .filter((edge) => {
      const sourcePorts = nodePortMap.get(edge.source);
      const targetPorts = nodePortMap.get(edge.target);
      if (!sourcePorts || !targetPorts) return false;
      const sourcePortName = (edge.sourceHandle ?? '').replace(/^port-/, '');
      const targetPortName = (edge.targetHandle ?? '').replace(/^port-/, '');
      if (!sourcePorts.has(sourcePortName)) return false;
      if (!targetPorts.has(targetPortName)) return false;
      return true;
    })
    .map((edge) => ({
      id: edge.id,
      sourceNodeId: edge.source,
      sourcePortName: (edge.sourceHandle ?? 'port-Output').replace(/^port-/, ''),
      targetNodeId: edge.target,
      targetPortName: (edge.targetHandle ?? 'port-Input').replace(/^port-/, ''),
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
      position: { x: ni.positionX ?? 0, y: ni.positionY ?? 0 },
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
    sourceHandle: conn.sourcePortName ? `port-${conn.sourcePortName}` : undefined,
    targetHandle: conn.targetPortName ? `port-${conn.targetPortName}` : undefined,
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

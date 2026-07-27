import type { Edge } from '@xyflow/react';
import type {
  Workflow,
  NodeDefinition,
  Connection,
  NodeTypeDescriptor,
  PortDefinition,
} from '../types/workflow.ts';
import type { WorkflowNodeData, WorkflowNode } from '../types/canvas.ts';
import { encodeHandleId, decodeHandleId } from './handleId.ts';
import { computeDynamicPorts } from './computeDynamicPorts.ts';

export function serializeWorkflow(
  nodes: WorkflowNode[],
  edges: Edge[],
  _name: string, // eslint-disable-line @typescript-eslint/no-unused-vars
): { nodeDefinitions: NodeDefinition[]; connections: Connection[] } {
  const nodeDefinitions: NodeDefinition[] = nodes.map((node) => {
    const data = node.data as WorkflowNodeData;
    // 使用完整动态端口集（含 Switch 等节点的运行时分支端口），避免分支连线在保存时丢失。
    const ports = computeDynamicPorts(data);
    return {
      id: node.id,
      typeName: data.typeName,
      name: data.name,
      parameters: data.parameters,
      ports,
      positionX: Math.round(node.position.x),
      positionY: Math.round(node.position.y),
      isEntry: data.isEntry,
      disabled: false,
      errorStrategy: data.errorStrategy,
      retryPolicy: data.retryPolicy,
      timeout: data.timeout,
    };
  });

  // 构建节点端口索引，用于保存时清理无效连接。复用上方已计算的完整动态端口集，确保动态分支连线不被误过滤。
  const nodePortMap = new Map<string, Set<string>>();
  for (const def of nodeDefinitions) {
    const ports = new Set<string>();
    for (const port of def.ports) {
      ports.add(port.name);
    }
    nodePortMap.set(def.id, ports);
  }

  const connections: Connection[] = edges
    .filter((edge) => {
      const sourcePorts = nodePortMap.get(edge.source);
      const targetPorts = nodePortMap.get(edge.target);
      if (!sourcePorts || !targetPorts) return false;
      const sourcePortName = decodeHandleId(edge.sourceHandle);
      const targetPortName = decodeHandleId(edge.targetHandle);
      if (!sourcePortName || !targetPortName) return false;
      if (!sourcePorts.has(sourcePortName)) return false;
      if (!targetPorts.has(targetPortName)) return false;
      return true;
    })
    .map((edge) => ({
      id: edge.id,
      sourceNodeId: edge.source,
      sourcePortName: decodeHandleId(edge.sourceHandle) || 'Output',
      targetNodeId: edge.target,
      targetPortName: decodeHandleId(edge.targetHandle) || 'Input',
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
    sourceHandle: conn.sourcePortName ? encodeHandleId(conn.sourcePortName) : undefined,
    targetHandle: conn.targetPortName ? encodeHandleId(conn.targetPortName) : undefined,
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
    categoryKey: 'unknown',
    icon: '',
    executionMode: 'Sync',
    parameters: [],
    ports: [...inputPorts, ...outputPorts],
    defaultIsEntry: ni.isEntry,
  };
}

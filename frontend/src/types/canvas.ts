import type { Node, Edge } from '@xyflow/react';
import type { NodeTypeDescriptor, RetryPolicyDto } from './workflow.ts';

export type WorkflowNodeData = {
  typeName: string;
  name: string;
  parameters: Record<string, unknown>;
  isEntry: boolean;
  descriptor: NodeTypeDescriptor;
  errorStrategy: string;
  retryPolicy: RetryPolicyDto | null;
  timeout: number | null;
  executionStatus?: 'idle' | 'running' | 'success' | 'error' | 'waiting';
};

export type WorkflowNode = Node<WorkflowNodeData, 'workflow'>;
export type WorkflowEdge = Edge;

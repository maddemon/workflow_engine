import { describe, it, expect } from 'vitest';
import type { NodeTypeDescriptor } from '../workflow.ts';
import type { WorkflowNode, WorkflowEdge, WorkflowNodeData } from '../canvas.ts';

describe('canvas types', () => {
  it('exports WorkflowNodeData, WorkflowNode and WorkflowEdge from types/canvas.ts', () => {
    // 引用这些类型名本身即证明它们已从新位置导出；再对样例对象做结构化校验。
    const descriptor = {} as NodeTypeDescriptor;
    const data: WorkflowNodeData = {
      typeName: 'httpRequest',
      name: 'HTTP Request',
      parameters: {},
      isEntry: true,
      descriptor,
      errorStrategy: 'Terminate',
      retryPolicy: null,
      timeout: null,
    };
    const node: WorkflowNode = {
      id: 'n1',
      type: 'workflow',
      position: { x: 0, y: 0 },
      data,
    };
    const edge: WorkflowEdge = { id: 'e1', source: 'n1', target: 'n2' };

    expect(node.type).toBe('workflow');
    expect(node.data.typeName).toBe('httpRequest');
    expect(edge.source).toBe('n1');
    expect(data.isEntry).toBe(true);
  });
});

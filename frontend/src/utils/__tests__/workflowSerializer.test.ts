import { describe, it, expect } from 'vitest';
import { serializeWorkflow, deserializeWorkflow } from '../workflowSerializer.ts';
import type { Edge } from '@xyflow/react';
import type { Workflow, NodeTypeDescriptor } from '../../types/workflow.ts';
import type { WorkflowNode } from '../../types/canvas.ts';
import { encodeHandleId } from '../handleId.ts';

function makeNodeType(typeName: string, portNames: { name: string; direction: 'Input' | 'Output' }[]): NodeTypeDescriptor {
  return {
    typeName,
    displayName: typeName,
    category: 'Test',
    categoryKey: 'logic',
    icon: '',
    executionMode: 'Sync',
    parameters: [],
    ports: portNames.map((p) => ({
      name: p.name,
      displayName: p.name,
      direction: p.direction,
      type: 'String',
      required: false,
    })),
    defaultIsEntry: false,
  };
}

function makeWorkflowNode(id: string, typeName: string, descriptor: NodeTypeDescriptor): WorkflowNode {
  return {
    id,
    type: 'workflow',
    position: { x: 10, y: 20 },
    data: {
      typeName,
      name: `${typeName}-${id}`,
      parameters: { url: 'http://example.com' },
      isEntry: false,
      descriptor,
      errorStrategy: 'Terminate',
      retryPolicy: null,
      timeout: null,
    },
  };
}

describe('workflowSerializer', () => {
  describe('serializeWorkflow', () => {
    it('serializes nodes and edges into definitions and connections', () => {
      const descriptor = makeNodeType('HttpRequest', [
        { name: 'Input', direction: 'Input' },
        { name: 'Output', direction: 'Output' },
      ]);
      const nodes: WorkflowNode[] = [
        makeWorkflowNode('n1', 'HttpRequest', descriptor),
        makeWorkflowNode('n2', 'HttpRequest', descriptor),
      ];
      const edges: Edge[] = [
        {
          id: 'e1',
          source: 'n1',
          target: 'n2',
          sourceHandle: 'port-Output',
          targetHandle: 'port-Input',
          type: 'workflow',
        },
      ];
      const result = serializeWorkflow(nodes, edges, 'test');
      expect(result.nodeDefinitions).toHaveLength(2);
      expect(result.nodeDefinitions[0]).toMatchObject({
        id: 'n1',
        typeName: 'HttpRequest',
        name: 'HttpRequest-n1',
        parameters: { url: 'http://example.com' },
        positionX: 10,
        positionY: 20,
        isEntry: false,
        disabled: false,
        errorStrategy: 'Terminate',
      });
      expect(result.connections).toHaveLength(1);
      expect(result.connections[0]).toMatchObject({
        id: 'e1',
        sourceNodeId: 'n1',
        sourcePortName: 'Output',
        targetNodeId: 'n2',
        targetPortName: 'Input',
      });
    });

    it('filters edges referencing missing ports', () => {
      const descriptor = makeNodeType('HttpRequest', [{ name: 'Input', direction: 'Input' }]);
      const nodes: WorkflowNode[] = [makeWorkflowNode('n1', 'HttpRequest', descriptor)];
      const edges: Edge[] = [
        {
          id: 'e1',
          source: 'n1',
          target: 'n1',
          sourceHandle: 'port-Missing',
          targetHandle: 'port-Input',
          type: 'workflow',
        },
      ];
      const result = serializeWorkflow(nodes, edges, 'test');
      expect(result.connections).toHaveLength(0);
    });

    it('filters edges referencing unknown nodes', () => {
      const descriptor = makeNodeType('HttpRequest', [
        { name: 'Input', direction: 'Input' },
        { name: 'Output', direction: 'Output' },
      ]);
      const nodes: WorkflowNode[] = [makeWorkflowNode('n1', 'HttpRequest', descriptor)];
      const edges: Edge[] = [
        {
          id: 'e1',
          source: 'n1',
          target: 'unknown',
          sourceHandle: 'port-Output',
          targetHandle: 'port-Input',
          type: 'workflow',
        },
      ];
      const result = serializeWorkflow(nodes, edges, 'test');
      expect(result.connections).toHaveLength(0);
    });

    it('skips edges when handles are absent and no defaults match', () => {
      const descriptor = makeNodeType('HttpRequest', [
        { name: 'Input', direction: 'Input' },
        { name: 'Output', direction: 'Output' },
      ]);
      const nodes: WorkflowNode[] = [
        makeWorkflowNode('n1', 'HttpRequest', descriptor),
        makeWorkflowNode('n2', 'HttpRequest', descriptor),
      ];
      const edges: Edge[] = [
        {
          id: 'e1',
          source: 'n1',
          target: 'n2',
          type: 'workflow',
        },
      ];
      const result = serializeWorkflow(nodes, edges, 'test');
      expect(result.connections).toHaveLength(0);
    });
  });

  describe('deserializeWorkflow', () => {
    it('deserializes workflow into nodes and edges', () => {
      const descriptor = makeNodeType('HttpRequest', [
        { name: 'Input', direction: 'Input' },
        { name: 'Output', direction: 'Output' },
      ]);
      const workflow: Workflow = {
        id: 'wf-1',
        name: 'Test Workflow',
        projectId: 'p1',
        version: 1,
        createdBy: 'user',
        createdAt: '2024-01-01',
        updatedAt: '2024-01-01',
        isActive: true,
        styleSettings: null,
        nodes: [
          {
            id: 'n1',
            typeName: 'HttpRequest',
            name: 'n1',
            parameters: {},
            ports: descriptor.ports,
            positionX: 100,
            positionY: 200,
            isEntry: false,
            disabled: false,
            errorStrategy: 'Terminate',
            retryPolicy: null,
            timeout: null,
          },
          {
            id: 'n2',
            typeName: 'HttpRequest',
            name: 'n2',
            parameters: {},
            ports: descriptor.ports,
            positionX: 300,
            positionY: 400,
            isEntry: false,
            disabled: false,
            errorStrategy: 'Terminate',
            retryPolicy: null,
            timeout: null,
          },
        ],
        connections: [
          {
            id: 'c1',
            sourceNodeId: 'n1',
            sourcePortName: 'Output',
            targetNodeId: 'n2',
            targetPortName: 'Input',
          },
        ],
      };
      const result = deserializeWorkflow(workflow, [descriptor]);
      expect(result.nodes).toHaveLength(2);
      expect(result.nodes[0].id).toBe('n1');
      expect(result.nodes[0].position).toEqual({ x: 100, y: 200 });
      expect(result.nodes[0].data.isEntry).toBe(true);
      expect(result.edges).toHaveLength(1);
      expect(result.edges[0]).toMatchObject({
        id: 'c1',
        source: 'n1',
        target: 'n2',
        sourceHandle: 'port-Output',
        targetHandle: 'port-Input',
        type: 'workflow',
      });
    });

    it('falls back to unknown descriptor when type is not available', () => {
      const workflow: Workflow = {
        id: 'wf-1',
        name: 'Test',
        projectId: 'p1',
        version: 1,
        createdBy: 'user',
        createdAt: '2024-01-01',
        updatedAt: '2024-01-01',
        isActive: true,
        styleSettings: null,
        nodes: [
          {
            id: 'n1',
            typeName: 'MissingType',
            name: 'n1',
            parameters: {},
            ports: [{ name: 'Output', displayName: 'Output', direction: 'Output', type: 'String', required: false }],
            positionX: 0,
            positionY: 0,
            isEntry: false,
            disabled: false,
            errorStrategy: 'Terminate',
            retryPolicy: null,
            timeout: null,
          },
        ],
        connections: [],
      };
      const result = deserializeWorkflow(workflow, []);
      expect(result.nodes).toHaveLength(1);
      expect(result.nodes[0].data.descriptor.typeName).toBe('MissingType');
      expect(result.nodes[0].data.descriptor.category).toBe('Unknown');
      expect(result.nodes[0].data.descriptor.ports).toHaveLength(1);
    });

    it('marks explicit entry nodes', () => {
      const descriptor = makeNodeType('HttpRequest', []);
      const workflow: Workflow = {
        id: 'wf-1',
        name: 'Test',
        projectId: 'p1',
        version: 1,
        createdBy: 'user',
        createdAt: '2024-01-01',
        updatedAt: '2024-01-01',
        isActive: true,
        styleSettings: null,
        nodes: [
          {
            id: 'n1',
            typeName: 'HttpRequest',
            name: 'n1',
            parameters: {},
            ports: [],
            positionX: 0,
            positionY: 0,
            isEntry: true,
            disabled: false,
            errorStrategy: 'Terminate',
            retryPolicy: null,
            timeout: null,
          },
        ],
        connections: [],
      };
      const result = deserializeWorkflow(workflow, [descriptor]);
      expect(result.nodes[0].data.isEntry).toBe(true);
    });

    it('encodes port names with spaces into safe handle ids (regression: merge Input 1/Input 2)', () => {
      const descriptor = makeNodeType('Merge', [
        { name: 'Input 1', direction: 'Input' },
        { name: 'Input 2', direction: 'Input' },
        { name: 'Output', direction: 'Output' },
      ]);
      const workflow: Workflow = {
        id: 'wf-1',
        name: 'Test',
        projectId: 'p1',
        version: 1,
        createdBy: 'user',
        createdAt: '2024-01-01',
        updatedAt: '2024-01-01',
        isActive: true,
        styleSettings: null,
        nodes: [
          {
            id: 'src',
            typeName: 'Merge',
            name: 'src',
            parameters: {},
            ports: [],
            positionX: 0,
            positionY: 0,
            isEntry: false,
            disabled: false,
            errorStrategy: 'Terminate',
            retryPolicy: null,
            timeout: null,
          },
          {
            id: 'dst',
            typeName: 'Merge',
            name: 'dst',
            parameters: {},
            ports: [],
            positionX: 0,
            positionY: 0,
            isEntry: false,
            disabled: false,
            errorStrategy: 'Terminate',
            retryPolicy: null,
            timeout: null,
          },
        ],
        connections: [
          { id: 'c1', sourceNodeId: 'src', sourcePortName: 'Output', targetNodeId: 'dst', targetPortName: 'Input 1' },
          { id: 'c2', sourceNodeId: 'src', sourcePortName: 'Output', targetNodeId: 'dst', targetPortName: 'Input 2' },
        ],
      };
      const result = deserializeWorkflow(workflow, [descriptor]);
      expect(result.edges).toHaveLength(2);
      // handle id 中不得含空格，否则 React Flow 无法锚定连线
      expect(result.edges[0].targetHandle).toBe('port-Input%201');
      expect(result.edges[1].targetHandle).toBe('port-Input%202');
      expect(result.edges[0].targetHandle).not.toContain(' ');
      expect(result.edges[1].targetHandle).not.toContain(' ');
    });
  });

  describe('serializeWorkflow - dynamic ports (Switch)', () => {
    function makeSwitchDescriptor(): NodeTypeDescriptor {
      return {
        typeName: 'Switch',
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
              type: 'Object',
              defaultValue: null,
              required: false,
              validationRules: [],
              displayRule: null,
              credentialType: null,
              options: [],
              fields: [
                {
                  name: 'name',
                  displayName: 'Name',
                  type: 'String',
                  defaultValue: null,
                  required: false,
                  validationRules: [],
                  displayRule: null,
                  credentialType: null,
                  options: [],
                },
                {
                  name: 'label',
                  displayName: 'Label',
                  type: 'String',
                  defaultValue: null,
                  required: false,
                  validationRules: [],
                  displayRule: null,
                  credentialType: null,
                  options: [],
                },
              ],
            },
          },
        ],
        ports: [
          { name: 'Input', displayName: 'Input', direction: 'Input', type: 'Main', required: false },
          { name: 'default', displayName: 'Default', direction: 'Output', type: 'Main', required: false },
        ],
        defaultIsEntry: false,
      };
    }

    function makeSwitchNode(id: string, cases: { name: string }[]): WorkflowNode {
      const descriptor = makeSwitchDescriptor();
      return {
        id,
        type: 'workflow',
        position: { x: 0, y: 0 },
        data: {
          typeName: 'Switch',
          name: id,
          parameters: { cases },
          isEntry: false,
          descriptor,
          errorStrategy: 'Terminate',
          retryPolicy: null,
          timeout: null,
        },
      };
    }

    it('includes dynamic case ports and keeps their branch connections', () => {
      const sw = makeSwitchNode('sw1', [{ name: 'case_0' }, { name: 'case_1' }]);
      const target = makeWorkflowNode('t1', 'HttpRequest', makeNodeType('HttpRequest', [
        { name: 'Input', direction: 'Input' },
        { name: 'Output', direction: 'Output' },
      ]));
      const nodes: WorkflowNode[] = [sw, target];
      const edges: Edge[] = [
        { id: 'e0', source: 'sw1', target: 't1', sourceHandle: encodeHandleId('case_0'), targetHandle: encodeHandleId('Input'), type: 'workflow' },
        { id: 'e1', source: 'sw1', target: 't1', sourceHandle: encodeHandleId('case_1'), targetHandle: encodeHandleId('Input'), type: 'workflow' },
      ];
      const result = serializeWorkflow(nodes, edges, 'test');

      const portNames = result.nodeDefinitions[0].ports.map((p) => p.name);
      expect(portNames).toContain('case_0');
      expect(portNames).toContain('case_1');

      expect(result.connections).toHaveLength(2);
      expect(result.connections.map((c) => c.sourcePortName ?? '').sort()).toEqual(['case_0', 'case_1']);
    });

    it('renames duplicate case names so serialized ports have no duplicates', () => {
      const sw = makeSwitchNode('sw1', [{ name: 'dup' }, { name: 'dup' }]);
      const target = makeWorkflowNode('t1', 'HttpRequest', makeNodeType('HttpRequest', [
        { name: 'Input', direction: 'Input' },
        { name: 'Output', direction: 'Output' },
      ]));
      const nodes: WorkflowNode[] = [sw, target];
      const result = serializeWorkflow(nodes, [], 'test');

      const outputPortNames = result.nodeDefinitions[0].ports
        .map((p) => p.name)
        .filter((n) => n !== 'Input' && n !== 'default');
      expect(new Set(outputPortNames).size).toBe(outputPortNames.length);
    });
  });

  describe('space-in-port-name round trip', () => {
    it('serializes encoded handles back to canonical port names (no %20 leakage)', () => {
      const descriptor = makeNodeType('Merge', [
        { name: 'Input 1', direction: 'Input' },
        { name: 'Output', direction: 'Output' },
      ]);
      const nodes: WorkflowNode[] = [makeWorkflowNode('n1', 'Merge', descriptor), makeWorkflowNode('n2', 'Merge', descriptor)];
      const edges: Edge[] = [
        {
          id: 'e1',
          source: 'n1',
          target: 'n2',
          sourceHandle: 'port-Output',
          targetHandle: 'port-Input%201',
          type: 'workflow',
        },
      ];
      const result = serializeWorkflow(nodes, edges, 'test');
      expect(result.connections).toHaveLength(1);
      expect(result.connections[0]).toMatchObject({
        sourcePortName: 'Output',
        targetPortName: 'Input 1',
      });
    });
  });
});

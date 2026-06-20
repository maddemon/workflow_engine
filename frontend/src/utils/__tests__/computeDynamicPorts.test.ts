import { describe, it, expect } from 'vitest';
import { computeDynamicPorts } from '../computeDynamicPorts';
import type { WorkflowNodeData } from '../../stores/workflowStore';
import type { NodeTypeDescriptor, PortDefinition } from '../../types/workflow';

function makeData(overrides: Partial<WorkflowNodeData> = {}): WorkflowNodeData {
  const staticPorts: PortDefinition[] = [
    { name: 'input', displayName: 'Input', direction: 'Input', type: 'Main' },
    { name: 'output', displayName: 'Output', direction: 'Output', type: 'Main' },
  ];

  const descriptor: NodeTypeDescriptor = {
    typeName: 'test',
    displayName: 'Test',
    category: 'Core',
    icon: 'box',
    executionMode: 'Sync',
    parameters: [],
    ports: staticPorts,
    defaultIsEntry: false,
    ...overrides.descriptor,
  };

  return {
    typeName: 'test',
    name: 'Test',
    parameters: {},
    isEntry: false,
    descriptor,
    errorStrategy: 'Terminate',
    retryPolicy: null,
    timeout: null,
    ...overrides,
  };
}

describe('computeDynamicPorts', () => {
  it('returns static ports for non-dynamic nodes', () => {
    const data = makeData();
    const ports = computeDynamicPorts(data);
    expect(ports).toHaveLength(2);
    expect(ports[0].name).toBe('input');
    expect(ports[1].name).toBe('output');
  });

  it('generates dynamic output ports for Switch node', () => {
    const data = makeData({
      descriptor: {
        typeName: 'switch',
        displayName: 'Switch',
        category: 'Core',
        icon: 'git-branch',
        executionMode: 'Sync',
        parameters: [
          {
            name: 'cases',
            displayName: 'Cases',
            type: 'Array',
            required: false,
            defaultValue: [],
            validationRules: [],
            options: [],
            description: null,
            hint: null,
            displayRule: null,
            credentialType: null,
            resourceType: null,
            itemDefinition: {
              name: 'cases',
              displayName: 'Cases',
              type: 'Array',
              required: false,
              defaultValue: [],
              validationRules: [],
              options: [],
              description: null,
              hint: null,
              displayRule: null,
              credentialType: null,
              resourceType: null,
              itemDefinition: null,
              fields: [
                { name: 'name', displayName: 'Name', type: 'String', required: false, defaultValue: '', validationRules: [], options: [], description: null, hint: null, displayRule: null, credentialType: null, resourceType: null, itemDefinition: null },
                { name: 'label', displayName: 'Label', type: 'String', required: false, defaultValue: '', validationRules: [], options: [], description: null, hint: null, displayRule: null, credentialType: null, resourceType: null, itemDefinition: null },
              ],
            },
          },
        ],
        ports: [
          { name: 'input', displayName: 'Input', direction: 'Input', type: 'Main' },
          { name: 'default', displayName: 'Default', direction: 'Output', type: 'Main' },
        ],
        defaultIsEntry: false,
      },
      parameters: {
        cases: [
          { name: 'case1', label: 'Case 1' },
          { name: 'case2', label: 'Case 2' },
        ],
      },
    });

    const ports = computeDynamicPorts(data);
    expect(ports).toHaveLength(4); // input + case1 + case2 + default
    expect(ports[0].name).toBe('input');
    expect(ports[1].name).toBe('case1');
    expect(ports[2].name).toBe('case2');
    expect(ports[3].name).toBe('default');
  });
});

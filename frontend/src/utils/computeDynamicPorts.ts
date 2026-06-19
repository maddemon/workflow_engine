import type { PortDefinition } from '../types/workflow.ts';
import type { WorkflowNode } from '../stores/workflowStore.ts';

/**
 * 对拥有动态端口的节点（如 Switch），根据当前参数值重新计算端口列表。
 * 静态端口原样返回。
 */
export function computeDynamicPorts(data: WorkflowNode['data']): PortDefinition[] {
  const staticPorts = data.descriptor.ports;

  const dynamicArrayParam = data.descriptor.parameters.find(
    (p) => p.type === 'Array' && p.itemDefinition?.fields && p.itemDefinition.fields.length > 0,
  );

  if (!dynamicArrayParam) {
    return staticPorts;
  }

  const items = data.parameters[dynamicArrayParam.name];
  if (!Array.isArray(items)) {
    return staticPorts;
  }

  const nameField = dynamicArrayParam.itemDefinition!.fields!.find((f) => f.name === 'name');
  const labelField = dynamicArrayParam.itemDefinition!.fields!.find((f) => f.name === 'label');
  if (!nameField) {
    return staticPorts;
  }

  const inputPorts = staticPorts.filter((p) => p.direction === 'Input');
  const dynamicOutputPorts: PortDefinition[] = items
    .map((item, index) => {
      const obj = item as Record<string, unknown>;
      const portName = String(obj.name || `case_${index}`);
      const portLabel = labelField ? String(obj.label ?? portName) : portName;
      return {
        name: portName,
        displayName: portLabel,
        direction: 'Output' as const,
        type: 'Main',
        required: false,
      };
    })
    .filter((p) => p.name.length > 0)
    .map((p, i, arr) => {
      const dupes = arr.filter((q) => q.name === p.name);
      if (dupes.length > 1) {
        return { ...p, name: `${p.name}_${i}` };
      }
      return p;
    });

  const defaultPort = staticPorts.find((p) => p.direction === 'Output' && p.name === 'default');
  const outputPorts = [...dynamicOutputPorts];
  if (defaultPort) {
    outputPorts.push(defaultPort);
  }

  return [...inputPorts, ...outputPorts];
}

import { memo, useLayoutEffect, useMemo } from 'react';
import { Handle, Position, useUpdateNodeInternals } from '@xyflow/react';
import type { NodeProps } from '@xyflow/react';
import { Text } from '@mantine/core';
import { Play, Check, X, Loader } from 'lucide-react';
import type { WorkflowNode } from '../../stores/workflowStore.ts';
import type { PortDefinition } from '../../types/workflow.ts';
import { NodeIcon } from '../common/NodeIcon.tsx';
import { getNodeCategoryColor } from '../../theme.ts';
import { computeDynamicPorts } from '../../utils/computeDynamicPorts.ts';
import { useWorkflowStore } from '../../stores/workflowStore.ts';

type PortLayout = {
  position: Position;
  percent: number;
};

function computeOutputWidth(n: number): number {
  return Math.max(68, n * 32);
}

function distributePercent(n: number, i: number): number {
  return n === 1 ? 50 : ((i + 1) / (n + 1)) * 100;
}

function computePortLayouts(
  inputPorts: PortDefinition[],
  outputPorts: PortDefinition[],
  layoutDirection: 'vertical' | 'horizontal',
): Map<string, PortLayout> {
  const layouts = new Map<string, PortLayout>();

  const isVertical = layoutDirection === 'vertical';
  const inputPos = isVertical ? Position.Top : Position.Left;
  const outputPos = isVertical ? Position.Bottom : Position.Right;

  for (let i = 0; i < inputPorts.length; i++) {
    layouts.set(inputPorts[i].name, {
      position: inputPos,
      percent: distributePercent(inputPorts.length, i),
    });
  }

  for (let i = 0; i < outputPorts.length; i++) {
    layouts.set(outputPorts[i].name, {
      position: outputPos,
      percent: distributePercent(outputPorts.length, i),
    });
  }

  return layouts;
}

const statusBadgeColor: Record<string, string> = {
  entry: 'var(--status-waiting)',
  running: 'var(--status-running)',
  success: 'var(--status-success)',
  error: 'var(--status-error)',
};

function CustomNodeComponent({ id, data, selected }: NodeProps<WorkflowNode>) {
  const ports = useMemo(() => computeDynamicPorts(data), [data]);
  const inputPorts = ports.filter((p) => p.direction === 'Input');
  const outputPorts = ports.filter((p) => p.direction === 'Output');
  const styleSettings = useWorkflowStore((s) => s.styleSettings);
  const edges = useWorkflowStore((s) => s.edges);
  const layoutDirection = styleSettings.layoutDirection;
  const isVertical = layoutDirection === 'vertical';

  const nodeWidth = computeOutputWidth(outputPorts.length);
  const nodeHeight = 64;

  const layouts = computePortLayouts(inputPorts, outputPorts, layoutDirection);

  const connectedHandles = useMemo(() => {
    const connected = new Set<string>();
    for (const edge of edges) {
      if (edge.source === id && edge.sourceHandle) connected.add(edge.sourceHandle);
      if (edge.target === id && edge.targetHandle) connected.add(edge.targetHandle);
    }
    return connected;
  }, [edges, id]);
  const status = data.executionStatus;
  const statusClass = status && status !== 'idle' ? `status-${status}` : '';
  const categoryColor = getNodeCategoryColor(data.descriptor.category);
  const updateNodeInternals = useUpdateNodeInternals();

  useLayoutEffect(() => {
    updateNodeInternals(id);
  }, [id, inputPorts.length, outputPorts.length, updateNodeInternals]);

  const template = data.descriptor.displayTemplate;
  let subtitle: string | null = null;
  if (template) {
    subtitle = template.replace(/\{\{(\w+)\}\}/g, (_, key) => {
      const val = data.parameters[key];
      if (val === null || val === undefined) return '-';
      return String(val);
    });
  }

  const badgeKey = data.isEntry && (!status || status === 'idle') ? 'entry' : (status ?? '');
  const badgeColor = statusBadgeColor[badgeKey];

  return (
    <div
      className={`custom-node-wrapper${statusClass ? ` ${statusClass}` : ''}${selected ? ' selected' : ''}`}
      style={{ width: nodeWidth, height: nodeHeight }}
    >
      <div
        className="custom-node"
        style={{
          width: nodeWidth,
          height: nodeHeight,
          borderColor: selected ? categoryColor : undefined,
          boxShadow: selected
            ? `0 0 0 2px color-mix(in srgb, ${categoryColor} 20%, transparent), 0 4px 12px color-mix(in srgb, ${categoryColor} 15%, transparent)`
            : undefined,
        }}
      >
        <div className="node-icon-box">
          <NodeIcon icon={data.descriptor.icon} size={26} color={categoryColor} />
        </div>

        {badgeColor && (
          <div className="node-badge" style={{ background: badgeColor }}>
            {data.isEntry && (!status || status === 'idle') && <Play size={7} color="var(--mantine-color-white)" fill="var(--mantine-color-white)" />}
            {status === 'running' && <Loader size={8} color="var(--mantine-color-white)" speed={2} />}
            {status === 'success' && <Check size={9} color="var(--mantine-color-white)" strokeWidth={3} />}
            {status === 'error' && <X size={9} color="var(--mantine-color-white)" strokeWidth={3} />}
          </div>
        )}
      </div>

      {inputPorts.map((port) => {
        const layout = layouts.get(port.name)!;
        const handleId = `port-${port.name}`;
        const isConnected = connectedHandles.has(handleId);
        return (
          <Handle
            key={port.name}
            type="target"
            position={layout.position}
            style={isVertical ? { left: `${layout.percent}%` } : { top: `${layout.percent}%` }}
            id={handleId}
            className={`port-handle port-input${isConnected ? ' port-connected' : ''}`}
          />
        );
      })}

      {outputPorts.map((port) => {
        const layout = layouts.get(port.name)!;
        const handleId = `port-${port.name}`;
        const isConnected = connectedHandles.has(handleId);
        return (
          <Handle
            key={port.name}
            type="source"
            position={layout.position}
            style={isVertical ? { left: `${layout.percent}%` } : { top: `${layout.percent}%` }}
            id={handleId}
            className={`port-handle port-output${isConnected ? ' port-connected' : ''}`}
          />
        );
      })}

      <div className="node-body-abs" style={{ top: nodeHeight + 6, maxWidth: nodeWidth * 2 }}>
        <Text size="xs" fw={600} className="node-name">
          {data.name}
        </Text>

        {subtitle && (
          <Text size="xs" c="dimmed" className="node-subtitle">
            {subtitle}
          </Text>
        )}
      </div>
    </div>
  );
}

export const CustomNode = memo(CustomNodeComponent);

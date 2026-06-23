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

const AI_PORT_TYPES = new Set(['AgentTool', 'LLMSupply', 'Memory']);

function isConfigNode(ports: PortDefinition[]): boolean {
  const outputs = ports.filter((p) => p.direction === 'Output');
  return outputs.length > 0 && outputs.every((p) => AI_PORT_TYPES.has(p.type));
}

function isToolNode(ports: PortDefinition[]): boolean {
  return ports.some((p) => p.direction === 'Output' && p.type === 'AgentTool');
}

function isConfigurableNode(ports: PortDefinition[]): boolean {
  return ports.some((p) => p.direction === 'Input' && AI_PORT_TYPES.has(p.type));
}

function isAiPort(port: PortDefinition): boolean {
  return AI_PORT_TYPES.has(port.type);
}

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

const AI_INPUT_SORT_ORDER: Record<string, number> = { LLMSupply: 0, Memory: 1, AgentTool: 2 };

function sortAiInputs(inputs: PortDefinition[]): PortDefinition[] {
  return [...inputs].sort((a, b) => (AI_INPUT_SORT_ORDER[a.type] ?? 99) - (AI_INPUT_SORT_ORDER[b.type] ?? 99));
}

function computePortLayouts(
  inputPorts: PortDefinition[],
  outputPorts: PortDefinition[],
  layoutDirection: 'vertical' | 'horizontal',
  configurable: boolean,
  config: boolean,
  tool: boolean,
): Map<string, PortLayout> {
  const layouts = new Map<string, PortLayout>();

  const isVertical = layoutDirection === 'vertical';

  const mainInputPos = isVertical ? Position.Top : Position.Left;
  const mainOutputPos = isVertical ? Position.Bottom : Position.Right;
  const aiInputPos = isVertical ? Position.Right : Position.Bottom;
  const aiOutputPos = isVertical ? Position.Left : Position.Top;

  if (tool) {
    const aiOutputs = outputPorts.filter((p) => p.type === 'AgentTool');
    for (let i = 0; i < aiOutputs.length; i++) {
      layouts.set(aiOutputs[i].name, {
        position: aiOutputPos,
        percent: distributePercent(aiOutputs.length, i),
      });
    }
    return layouts;
  }

  if (config) {
    for (let i = 0; i < outputPorts.length; i++) {
      layouts.set(outputPorts[i].name, {
        position: aiOutputPos,
        percent: distributePercent(outputPorts.length, i),
      });
    }
    return layouts;
  }

  if (configurable) {
    const mainInputs = inputPorts.filter((p) => !isAiPort(p));
    const aiInputs = sortAiInputs(inputPorts.filter((p) => isAiPort(p)));
    const mainOutputs = outputPorts.filter((p) => !isAiPort(p));
    const aiOutputs = outputPorts.filter((p) => isAiPort(p));

    for (let i = 0; i < mainInputs.length; i++) {
      layouts.set(mainInputs[i].name, {
        position: mainInputPos,
        percent: distributePercent(mainInputs.length, i),
      });
    }
    for (let i = 0; i < aiInputs.length; i++) {
      layouts.set(aiInputs[i].name, {
        position: aiInputPos,
        percent: distributePercent(aiInputs.length, i),
      });
    }
    for (let i = 0; i < mainOutputs.length; i++) {
      layouts.set(mainOutputs[i].name, {
        position: mainOutputPos,
        percent: distributePercent(mainOutputs.length, i),
      });
    }
    for (let i = 0; i < aiOutputs.length; i++) {
      layouts.set(aiOutputs[i].name, {
        position: aiOutputPos,
        percent: distributePercent(aiOutputs.length, i),
      });
    }
  } else {
    for (let i = 0; i < inputPorts.length; i++) {
      layouts.set(inputPorts[i].name, {
        position: mainInputPos,
        percent: distributePercent(inputPorts.length, i),
      });
    }
    for (let i = 0; i < outputPorts.length; i++) {
      layouts.set(outputPorts[i].name, {
        position: mainOutputPos,
        percent: distributePercent(outputPorts.length, i),
      });
    }
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

  const config = isConfigNode(ports);
  const tool = isToolNode(ports);
  const configurable = isConfigurableNode(ports);

  const circular = config || tool;

  const nodeWidth = circular ? 56 : configurable ? Math.max(160, outputPorts.length * 32) : computeOutputWidth(outputPorts.length);
  const nodeHeight = circular ? 56 : 64;

  const layouts = computePortLayouts(inputPorts, outputPorts, layoutDirection, configurable, config, tool);

  const visibleInputPorts = tool ? [] : inputPorts;
  const visibleOutputPorts = tool ? outputPorts.filter((p) => p.type === 'AgentTool') : outputPorts;

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

  const badgeKey = (!circular && data.isEntry && (!status || status === 'idle')) ? 'entry' : (status ?? '');
  const badgeColor = statusBadgeColor[badgeKey];

  const nodeClasses = [
    'custom-node-wrapper',
    statusClass ? ` ${statusClass}` : '',
    selected ? ' selected' : '',
    circular ? ' node-circular' : '',
    configurable ? ' node-configurable' : '',
  ].join('');

  return (
    <div
      className={nodeClasses}
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
        {configurable && !config ? (
          <>
            <div className="node-icon-box node-icon-left">
              <NodeIcon icon={data.descriptor.icon} size={26} color={categoryColor} />
            </div>
            <div className="node-body-inline">
              <Text size="xs" fw={600} className="node-name">{data.name}</Text>
              {subtitle && (
                <Text size="xs" c="dimmed" className="node-subtitle">{subtitle}</Text>
              )}
            </div>
          </>
        ) : (
          <>
            <div className="node-icon-box">
              <NodeIcon icon={data.descriptor.icon} size={circular ? 24 : 26} color={categoryColor} />
            </div>
            {!circular && (
              <div className="node-body-abs" style={{ top: nodeHeight + 6, maxWidth: nodeWidth * 2 }}>
                <Text size="xs" fw={600} className="node-name">
                  {data.name}
                </Text>
                {subtitle && (
                  <Text size="xs" c="dimmed" className="node-subtitle">{subtitle}</Text>
                )}
              </div>
            )}
          </>
        )}

        {badgeColor && (
          <div className="node-badge" style={{ background: badgeColor }}>
            {data.isEntry && (!status || status === 'idle') && <Play size={7} color="var(--mantine-color-white)" fill="var(--mantine-color-white)" />}
            {status === 'running' && <Loader size={8} color="var(--mantine-color-white)" speed={2} />}
            {status === 'success' && <Check size={9} color="var(--mantine-color-white)" strokeWidth={3} />}
            {status === 'error' && <X size={9} color="var(--mantine-color-white)" strokeWidth={3} />}
          </div>
        )}
      </div>

      {visibleInputPorts.map((port) => {
        const layout = layouts.get(port.name);
        if (!layout) return null;
        const handleId = `port-${port.name}`;
        const isConnected = connectedHandles.has(handleId);
        const isVerticalEdge = layout.position === Position.Left || layout.position === Position.Right;
        const posClass = layout.position === Position.Bottom ? 'port-bottom'
          : layout.position === Position.Top ? 'port-top'
          : layout.position === Position.Left ? 'port-left'
          : 'port-right';
        return (
          <Handle
            key={port.name}
            type="target"
            position={layout.position}
            style={isVerticalEdge ? { top: `${layout.percent}%` } : { left: `${layout.percent}%` }}
            id={handleId}
            className={`port-handle port-input${isConnected ? ' port-connected' : ''}${isAiPort(port) ? ' port-ai' : ''} ${posClass}`}
          />
        );
      })}

      {visibleOutputPorts.map((port) => {
        const layout = layouts.get(port.name);
        if (!layout) return null;
        const handleId = `port-${port.name}`;
        const isConnected = connectedHandles.has(handleId);
        const isVerticalEdge = layout.position === Position.Left || layout.position === Position.Right;
        const posClass = layout.position === Position.Bottom ? 'port-bottom'
          : layout.position === Position.Top ? 'port-top'
          : layout.position === Position.Left ? 'port-left'
          : 'port-right';
        return (
          <Handle
            key={port.name}
            type="source"
            position={layout.position}
            style={isVerticalEdge ? { top: `${layout.percent}%` } : { left: `${layout.percent}%` }}
            id={handleId}
            className={`port-handle port-output${isConnected ? ' port-connected' : ''}${isAiPort(port) ? ' port-ai' : ''} ${posClass}`}
          />
        );
      })}

      {circular && (
        <div className="node-body-abs" style={{ top: nodeHeight + 6 }}>
          <Text size="xs" fw={600} className="node-name">
            {data.name}
          </Text>
          {subtitle && (
            <Text size="xs" c="dimmed" className="node-subtitle">{subtitle}</Text>
          )}
        </div>
      )}
    </div>
  );
}

export const CustomNode = memo(CustomNodeComponent);

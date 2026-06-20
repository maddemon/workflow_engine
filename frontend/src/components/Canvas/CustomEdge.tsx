import { memo, useState } from 'react';
import {
  getBezierPath,
  getSmoothStepPath,
  Position,
  useReactFlow,
  EdgeLabelRenderer,
} from '@xyflow/react';
import type { EdgeProps } from '@xyflow/react';
import type { WorkflowNode } from '../../stores/workflowStore.ts';
import { computeDynamicPorts } from '../../utils/computeDynamicPorts.ts';
import { useWorkflowStore } from '../../stores/workflowStore.ts';

const HANDLE_SIZE = 20;
const EDGE_PADDING = 130;
const EDGE_BORDER_RADIUS = 16;

function CustomEdgeComponent({
  id,
  source,
  sourceHandleId,
  sourceX,
  sourceY,
  targetX,
  targetY,
  sourcePosition,
  targetPosition,
  selected,
}: EdgeProps) {
  const [hovered, setHovered] = useState(false);
  const { getNode } = useReactFlow();
  const sourceNode = getNode(source);
  const sourceData = sourceNode?.data as WorkflowNode['data'] | undefined;
  const styleSettings = useWorkflowStore((s) => s.styleSettings);
  const isHorizontal = styleSettings.layoutDirection === 'horizontal';

  let label: string | null = null;
  if (sourceData && sourceHandleId) {
    const ports = computeDynamicPorts(sourceData);
    const outputPorts = ports.filter((p) => p.direction === 'Output');
    if (outputPorts.length > 1) {
      const port = outputPorts.find((p) => sourceHandleId === `port-${p.name}`);
      if (port) label = port.displayName;
    }
  }

  const isBackward = isHorizontal
    ? sourceX - HANDLE_SIZE > targetX
    : sourceY - HANDLE_SIZE > targetY;

  let edgePaths: string[];
  let labelX: number;
  let labelY: number;

  if (isBackward) {
    if (isHorizontal) {
      const midX = (sourceX + targetX) / 2;
      const midY = sourceY + EDGE_PADDING;

      const [path1, , , pos1X, pos1Y] = getSmoothStepPath({
        sourceX, sourceY,
        targetX: midX, targetY: midY,
        sourcePosition,
        targetPosition: Position.Right,
        borderRadius: EDGE_BORDER_RADIUS,
      });

      const [path2] = getSmoothStepPath({
        sourceX: midX, sourceY: midY,
        targetX, targetY,
        sourcePosition: Position.Left,
        targetPosition,
        borderRadius: EDGE_BORDER_RADIUS,
      });

      edgePaths = [path1, path2];
      labelX = pos1X;
      labelY = pos1Y;
    } else {
      const midX = sourceX + EDGE_PADDING;
      const midY = (sourceY + targetY) / 2;

      const [path1, , , pos1X, pos1Y] = getSmoothStepPath({
        sourceX, sourceY,
        targetX: midX, targetY: midY,
        sourcePosition,
        targetPosition: Position.Bottom,
        borderRadius: EDGE_BORDER_RADIUS,
      });

      const [path2] = getSmoothStepPath({
        sourceX: midX, sourceY: midY,
        targetX, targetY,
        sourcePosition: Position.Top,
        targetPosition,
        borderRadius: EDGE_BORDER_RADIUS,
      });

      edgePaths = [path1, path2];
      labelX = pos1X;
      labelY = pos1Y;
    }
  } else {
    const [edgePath, lx, ly] = getBezierPath({
      sourceX, sourceY,
      sourcePosition,
      targetX, targetY,
      targetPosition,
    });
    edgePaths = [edgePath];
    labelX = lx;
    labelY = ly;
  }

  const strokeColor = selected
    ? 'var(--edge-color-hover)'
    : hovered
      ? 'var(--edge-color-hover)'
      : 'var(--edge-color)';

  return (
    <>
      {edgePaths.map((path, index) => (
        <path
          key={index}
          d={path}
          fill="none"
          stroke="transparent"
          strokeWidth={20}
          onMouseEnter={() => setHovered(true)}
          onMouseLeave={() => setHovered(false)}
          style={{ cursor: 'pointer' }}
        />
      ))}
      {edgePaths.map((path, index) => (
        <path
          key={`edge-${index}`}
          d={path}
          className="react-flow__edge-path"
          style={{
            fill: 'none',
            strokeWidth: selected || hovered ? 2.5 : 2,
            stroke: strokeColor,
            pointerEvents: 'none',
            transition: 'stroke 0.15s, stroke-width 0.15s',
          }}
          markerEnd={index === edgePaths.length - 1 ? `url(#edge-arrow-${id})` : undefined}
        />
      ))}
      <defs>
        <marker
          id={`edge-arrow-${id}`}
          viewBox="0 0 10 10"
          refX="9"
          refY="5"
          markerWidth="5"
          markerHeight="5"
          orient="auto-start-reverse"
        >
          <path d="M 0 0 L 10 5 L 0 10 z" fill={strokeColor} />
        </marker>
      </defs>
      {label && (
        <EdgeLabelRenderer>
          <div
            style={{
              position: 'absolute',
              transform: `translate(-50%, -50%) translate(${labelX}px,${labelY}px)`,
              fontSize: 10,
              color: 'var(--edge-label-color)',
              background: 'var(--edge-label-bg)',
              pointerEvents: 'none',
              padding: '2px 8px',
              borderRadius: 10,
              fontWeight: 600,
              letterSpacing: '0.02em',
              boxShadow: '0 1px 3px rgba(0, 0, 0, 0.1)',
              border: '1px solid var(--mantine-color-gray-2)',
            }}
            className="nodrag nopan"
          >
            {label}
          </div>
        </EdgeLabelRenderer>
      )}
    </>
  );
}

export const CustomEdge = memo(CustomEdgeComponent);

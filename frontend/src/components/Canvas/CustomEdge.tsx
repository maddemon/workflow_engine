import type { EdgeProps } from "@xyflow/react"
import { EdgeLabelRenderer, getBezierPath, getSmoothStepPath, Position, useReactFlow } from "@xyflow/react"
import { memo, useState } from "react"
import type { WorkflowNode } from "../../stores/workflowStore.ts"
import { useWorkflowStore } from "../../stores/workflowStore.ts"
import { computeDynamicPorts } from "../../utils/computeDynamicPorts.ts"

const HANDLE_SIZE = 20
const EDGE_PADDING = 130
const EDGE_BORDER_RADIUS = 16
const AI_PORT_TYPES = new Set(["AgentTool", "LLM", "Memory"])

function isAiPortHandle(nodeData: WorkflowNode["data"] | undefined, handleId: string | null | undefined): boolean {
  if (!nodeData || !handleId) return false
  const ports = computeDynamicPorts(nodeData)
  return ports.some((p) => AI_PORT_TYPES.has(p.type) && handleId === `port-${p.name}`)
}

function CustomEdgeComponent({
  id,
  source,
  target,
  sourceHandleId,
  targetHandleId,
  sourceX,
  sourceY,
  targetX,
  targetY,
  sourcePosition,
  targetPosition,
  selected,
}: EdgeProps) {
  const [hovered, setHovered] = useState(false)
  const { getNode } = useReactFlow()
  const sourceNode = getNode(source)
  const targetNode = getNode(target)
  const sourceData = sourceNode?.data as WorkflowNode["data"] | undefined
  const targetData = targetNode?.data as WorkflowNode["data"] | undefined
  const styleSettings = useWorkflowStore((s) => s.styleSettings)
  const isHorizontal = styleSettings.layoutDirection === "horizontal"

  const isAiEdge = isAiPortHandle(sourceData, sourceHandleId) || isAiPortHandle(targetData, targetHandleId)

  let label: string | null = null
  if (!isAiEdge && sourceData && sourceHandleId) {
    const ports = computeDynamicPorts(sourceData)
    const outputPorts = ports.filter((p) => p.direction === "Output")
    if (outputPorts.length > 1) {
      const port = outputPorts.find((p) => sourceHandleId === `port-${p.name}`)
      if (port) label = port.displayName
    }
  }

  const isBackward = isHorizontal ? sourceX - HANDLE_SIZE > targetX : sourceY - HANDLE_SIZE > targetY

  let edgePaths: string[]
  let labelX: number
  let labelY: number

  if (isBackward && !isAiEdge) {
    if (isHorizontal) {
      const midX = (sourceX + targetX) / 2
      const midY = sourceY + EDGE_PADDING

      const [path1, , , pos1X, pos1Y] = getSmoothStepPath({
        sourceX,
        sourceY,
        targetX: midX,
        targetY: midY,
        sourcePosition,
        targetPosition: Position.Right,
        borderRadius: EDGE_BORDER_RADIUS,
      })

      const [path2] = getSmoothStepPath({
        sourceX: midX,
        sourceY: midY,
        targetX,
        targetY,
        sourcePosition: Position.Left,
        targetPosition,
        borderRadius: EDGE_BORDER_RADIUS,
      })

      edgePaths = [path1, path2]
      labelX = pos1X
      labelY = pos1Y
    } else {
      const midX = sourceX + EDGE_PADDING
      const midY = (sourceY + targetY) / 2

      const [path1, , , pos1X, pos1Y] = getSmoothStepPath({
        sourceX,
        sourceY,
        targetX: midX,
        targetY: midY,
        sourcePosition,
        targetPosition: Position.Bottom,
        borderRadius: EDGE_BORDER_RADIUS,
      })

      const [path2] = getSmoothStepPath({
        sourceX: midX,
        sourceY: midY,
        targetX,
        targetY,
        sourcePosition: Position.Top,
        targetPosition,
        borderRadius: EDGE_BORDER_RADIUS,
      })

      edgePaths = [path1, path2]
      labelX = pos1X
      labelY = pos1Y
    }
  } else {
    const [edgePath, lx, ly] = getBezierPath({
      sourceX,
      sourceY,
      sourcePosition,
      targetX,
      targetY,
      targetPosition,
    })
    edgePaths = [edgePath]
    labelX = lx
    labelY = ly
  }

  const strokeColor = selected ? "var(--edge-color-hover)" : hovered ? "var(--edge-color-hover)" : "var(--edge-color)"
  const strokeWidth = selected || hovered ? 2 : 1.5

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
          className="edge-hitarea"
        />
      ))}
      {edgePaths.map((path, index) => (
        <path
          key={`edge-${index}`}
          d={path}
          className="react-flow__edge-path edge-visible-path"
          style={{
            strokeWidth,
            stroke: strokeColor,
            strokeDasharray: isAiEdge ? "6 4" : undefined,
          }}
          markerEnd={!isAiEdge && index === edgePaths.length - 1 ? `url(#edge-arrow-${id})` : undefined}
        />
      ))}
      {!isAiEdge && (
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
      )}
      {label && (
        <EdgeLabelRenderer>
          <div
            className="nodrag nopan edge-label"
            style={{
              transform: `translate(-50%, -50%) translate(${labelX}px,${labelY}px)`,
            }}
          >
            {label}
          </div>
        </EdgeLabelRenderer>
      )}
    </>
  )
}

export const CustomEdge = memo(CustomEdgeComponent)

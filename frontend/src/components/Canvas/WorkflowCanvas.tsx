import { notifications } from "@mantine/notifications"
import { Background, BackgroundVariant, MiniMap, ReactFlow, useReactFlow, type Connection } from "@xyflow/react"
import "@xyflow/react/dist/style.css"
import { useCallback, useEffect, useMemo, useRef } from "react"
import { useWorkflowStore } from "../../stores/workflowStore.ts"
import { CanvasToolbar } from "./CanvasToolbar.tsx"
import { CustomEdge } from "./CustomEdge.tsx"
import { CustomNode } from "./CustomNode.tsx"

const nodeTypes = { workflow: CustomNode }
const edgeTypes = { workflow: CustomEdge }

const defaultEdgeOptions = {
  type: "workflow" as const,
  animated: false,
}

interface IWorkflowCanvasProps {
  onExecute: (workflowId: string) => void
}

export function WorkflowCanvas({ onExecute }: IWorkflowCanvasProps) {
  const reactFlowWrapper = useRef<HTMLDivElement>(null)
  const { screenToFlowPosition } = useReactFlow()
  const nodesData = useWorkflowStore((s) => s.nodes)
  const nodePositions = useWorkflowStore((s) => s.nodePositions)
  const edges = useWorkflowStore((s) => s.edges)
  const onNodesChange = useWorkflowStore((s) => s.onNodesChange)
  const onEdgesChange = useWorkflowStore((s) => s.onEdgesChange)
  const addEdge = useWorkflowStore((s) => s.addEdge)
  const addNode = useWorkflowStore((s) => s.addNode)
  const setSelectedNode = useWorkflowStore((s) => s.setSelectedNode)
  const isExecuting = useWorkflowStore((s) => s.isExecuting)

  const hasPositionOverrides = Object.keys(nodePositions).length > 0
  const nodes = useMemo(
    () => hasPositionOverrides
      ? nodesData.map((n) => {
          const pos = nodePositions[n.id]
          return pos ? { ...n, position: pos } : n
        })
      : nodesData,
    [nodesData, nodePositions, hasPositionOverrides],
  )

  const edgesRef = useRef(edges)
  useEffect(() => {
    edgesRef.current = edges
  }, [edges])

  const onConnect = useCallback(
    (params: Connection) => {
      const { source, sourceHandle, target } = params
      let { targetHandle } = params

      if (source === target) {
        notifications.show({
          title: "Connection rejected",
          message: "A node cannot connect to itself.",
          color: "red",
        })
        return
      }

      const sourceNode = nodes.find((n) => n.id === source)
      const targetNode = nodes.find((n) => n.id === target)

      if (targetNode && !targetHandle) {
        const firstInput = targetNode.data.descriptor.ports.find((p) => p.direction === "Input")
        if (firstInput) {
          targetHandle = `port-${firstInput.name}`
        }
      }

      if (sourceNode && sourceHandle) {
        const port = sourceNode.data.descriptor.ports.find((p) => `port-${p.name}` === sourceHandle)
        if (port && port.direction !== "Output") {
          notifications.show({
            title: "Connection rejected",
            message: "Source port must be an output port.",
            color: "red",
          })
          return
        }
      }
      if (targetNode && targetHandle) {
        const port = targetNode.data.descriptor.ports.find((p) => `port-${p.name}` === targetHandle)
        if (port && port.direction !== "Input") {
          notifications.show({
            title: "Connection rejected",
            message: "Target port must be an input port.",
            color: "red",
          })
          return
        }
      }

      const sourcePort = sourceNode?.data.descriptor.ports.find((p) => `port-${p.name}` === sourceHandle)
      const targetPort = targetNode?.data.descriptor.ports.find((p) => `port-${p.name}` === targetHandle)

      if (sourcePort && targetPort) {
        const compatible =
          sourcePort.type === targetPort.type ||
          (sourcePort.type === "AgentTool" && targetPort.type === "Main")
        if (!compatible) {
          notifications.show({
            title: "Connection rejected",
            message: `Port type '${sourcePort.type}' cannot connect to '${targetPort.type}'.`,
            color: "red",
          })
          return
        }
      }

      if (targetPort && targetHandle) {
        const maxConnections: Record<string, number> = { LLMSupply: 1, Memory: 1 }
        const max = maxConnections[targetPort.type]
        if (max !== undefined) {
          const existingCount = edgesRef.current.filter(
            (e) => e.target === target && e.targetHandle === targetHandle,
          ).length
          if (existingCount >= max) {
            notifications.show({
              title: "Connection rejected",
              message: `Port '${targetPort.displayName}' accepts at most ${max} connection${max > 1 ? "s" : ""}.`,
              color: "red",
            })
            return
          }
        }
      }

      const isDuplicate = edgesRef.current.some(
        (e) =>
          e.source === source &&
          e.sourceHandle === sourceHandle &&
          e.target === target &&
          e.targetHandle === targetHandle,
      )
      if (isDuplicate) {
        notifications.show({
          title: "Connection rejected",
          message: "This connection already exists.",
          color: "yellow",
        })
        return
      }

      addEdge(source, sourceHandle, target, targetHandle)
    },
    [addEdge, nodes],
  )

  const onNodeClick = useCallback(
    (_: React.MouseEvent, node: { id: string }) => {
      setSelectedNode(node.id)
    },
    [setSelectedNode],
  )

  const onPaneClick = useCallback(() => {
    setSelectedNode(null)
  }, [setSelectedNode])

  const onDragOver = useCallback(
    (event: React.DragEvent) => {
      if (isExecuting) return
      event.preventDefault()
      event.dataTransfer.dropEffect = "move"
    },
    [isExecuting],
  )

  const onDrop = useCallback(
    (event: React.DragEvent) => {
      if (isExecuting) return
      event.preventDefault()
      const typeName = event.dataTransfer.getData("application/reactflow")
      if (!typeName) return

      const position = screenToFlowPosition({
        x: event.clientX,
        y: event.clientY,
      })
      addNode(typeName, position)
    },
    [addNode, isExecuting, screenToFlowPosition],
  )

  return (
    <div style={{ display: "flex", flexDirection: "column", height: "100%" }}>
      <CanvasToolbar onExecute={onExecute} />
      <div ref={reactFlowWrapper} className="workflow-canvas">
        <ReactFlow
          nodes={nodes}
          edges={edges}
          nodesDraggable={!isExecuting}
          nodesConnectable={!isExecuting}
          elementsSelectable={!isExecuting}
          onNodesChange={onNodesChange}
          onEdgesChange={onEdgesChange}
          onConnect={onConnect}
          onNodeClick={onNodeClick}
          onPaneClick={onPaneClick}
          onDragOver={onDragOver}
          onDrop={onDrop}
          nodeTypes={nodeTypes}
          edgeTypes={edgeTypes}
          defaultEdgeOptions={defaultEdgeOptions}
        >
          <Background variant={BackgroundVariant.Lines} gap={200} color="rgba(128, 128, 128, 0.1)" size={1} />
          <MiniMap pannable zoomable />
        </ReactFlow>
      </div>
    </div>
  )
}

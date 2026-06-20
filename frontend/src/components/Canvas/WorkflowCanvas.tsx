import { notifications } from "@mantine/notifications"
import { Background, BackgroundVariant, MiniMap, ReactFlow, useReactFlow, type Connection } from "@xyflow/react"
import "@xyflow/react/dist/style.css"
import { useCallback, useEffect, useRef } from "react"
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
  const nodes = useWorkflowStore((s) => s.nodes)
  const edges = useWorkflowStore((s) => s.edges)
  const onNodesChange = useWorkflowStore((s) => s.onNodesChange)
  const onEdgesChange = useWorkflowStore((s) => s.onEdgesChange)
  const addEdge = useWorkflowStore((s) => s.addEdge)
  const addNode = useWorkflowStore((s) => s.addNode)
  const setSelectedNode = useWorkflowStore((s) => s.setSelectedNode)
  const isExecuting = useWorkflowStore((s) => s.isExecuting)

  const edgesRef = useRef(edges)
  useEffect(() => {
    edgesRef.current = edges
  }, [edges])

  const onConnect = useCallback(
    (params: Connection) => {
      const { source, sourceHandle, target } = params
      let { targetHandle } = params

      // 禁止自连接
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

      // 鼠标松在节点区域（非 input handle 上）时，自动连到第一个 input 端口
      if (targetNode && !targetHandle) {
        const firstInput = targetNode.data.descriptor.ports.find((p) => p.direction === "Input")
        if (firstInput) {
          targetHandle = `port-${firstInput.name}`
        }
      }

      // 校验端口类型：source 必须是 output，target 必须是 input
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

      // 禁止重复连接
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

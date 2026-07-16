import { notifications } from "@mantine/notifications"
import { Background, BackgroundVariant, MiniMap, ReactFlow, useReactFlow, type Connection } from "@xyflow/react"
import "@xyflow/react/dist/style.css"
import { useCallback, useEffect, useMemo, useRef } from "react"
import { useTranslation } from "react-i18next"
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
  onCancel?: () => void
  onDryRun?: () => void
  dryRunLoading?: boolean
}

export function WorkflowCanvas({ onExecute, onCancel, onDryRun, dryRunLoading }: IWorkflowCanvasProps) {
  const { t } = useTranslation(['workflow', 'common'])
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
  const reviewMode = useWorkflowStore((s) => s.reviewMode)
  const copyNode = useWorkflowStore((s) => s.copyNode)
  const pasteNode = useWorkflowStore((s) => s.pasteNode)

  const hasPositionOverrides = Object.keys(nodePositions).length > 0
  const nodes = useMemo(
    () =>
      hasPositionOverrides
        ? nodesData.map((n) => {
            const pos = nodePositions[n.id]
            return pos ? { ...n, position: pos } : n
          })
        : nodesData,
    [nodesData, nodePositions, hasPositionOverrides],
  )

  const onConnect = useCallback(
    (params: Connection) => {
      const { source, sourceHandle, target } = params
      let { targetHandle } = params

      if (source === target) {
        notifications.show({
          title: t('canvasConnectionRejected'),
          message: t('canvasNodeSelfConnect'),
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
            title: t('canvasConnectionRejected'),
            message: t('canvasSourcePortOutput'),
            color: "red",
          })
          return
        }
      }
      if (targetNode && targetHandle) {
        const port = targetNode.data.descriptor.ports.find((p) => `port-${p.name}` === targetHandle)
        if (port && port.direction !== "Input") {
          notifications.show({
            title: t('canvasConnectionRejected'),
            message: t('canvasTargetPortInput'),
            color: "red",
          })
          return
        }
      }

      const sourcePort = sourceNode?.data.descriptor.ports.find((p) => `port-${p.name}` === sourceHandle)
      const targetPort = targetNode?.data.descriptor.ports.find((p) => `port-${p.name}` === targetHandle)

      if (sourcePort && targetPort) {
        const compatible =
          sourcePort.type === targetPort.type || (sourcePort.type === "AgentTool" && targetPort.type === "Main")
        if (!compatible) {
          notifications.show({
            title: t('canvasConnectionRejected'),
            message: t('canvasPortTypeMismatch', { sourceType: sourcePort.type, targetType: targetPort.type }),
            color: "red",
          })
          return
        }
      }

      if (targetPort && targetHandle) {
        const maxConnections: Record<string, number> = { LLM: 1, Memory: 1 }
        const max = maxConnections[targetPort.type]
        if (max !== undefined) {
          const existingCount = useWorkflowStore.getState().edges.filter(
            (e) => e.target === target && e.targetHandle === targetHandle,
          ).length
          if (existingCount >= max) {
            notifications.show({
              title: t('canvasConnectionRejected'),
              message: t('canvasPortMaxConnections', { displayName: targetPort.displayName, max }),
              color: "red",
            })
            return
          }
        }
      }

      const isDuplicate = useWorkflowStore.getState().edges.some(
        (e) =>
          e.source === source &&
          e.sourceHandle === sourceHandle &&
          e.target === target &&
          e.targetHandle === targetHandle,
      )
      if (isDuplicate) {
        notifications.show({
          title: t('canvasConnectionRejected'),
          message: t('canvasConnectionExists'),
          color: "yellow",
        })
        return
      }

      addEdge(source, sourceHandle, target, targetHandle)
    },
    [addEdge, nodes, t],
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
      if (isExecuting || reviewMode) return
      event.preventDefault()
      event.dataTransfer.dropEffect = "move"
    },
    [isExecuting, reviewMode],
  )

  const onDrop = useCallback(
    (event: React.DragEvent) => {
      if (isExecuting || reviewMode) return
      event.preventDefault()
      const typeName = event.dataTransfer.getData("application/reactflow")
      if (!typeName) return

      const position = screenToFlowPosition({
        x: event.clientX,
        y: event.clientY,
      })
      addNode(typeName, position)
    },
    [addNode, isExecuting, reviewMode, screenToFlowPosition],
  )

  useEffect(() => {
    const handler = (e: KeyboardEvent) => {
      if (!(e.ctrlKey || e.metaKey)) return
      const target = e.target as HTMLElement | null
      if (target && (target.tagName === "INPUT" || target.tagName === "TEXTAREA" || target.isContentEditable)) return
      const selectedId = useWorkflowStore.getState().selectedNodeId
      if (e.key.toLowerCase() === "c" && selectedId) {
        e.preventDefault()
        copyNode(selectedId)
        notifications.show({ title: t('copied', { ns: 'common' }), message: t('nodeCopied'), color: "teal" })
      } else if (e.key.toLowerCase() === "v") {
        const wrapper = reactFlowWrapper.current
        if (!wrapper) return
        const rect = wrapper.getBoundingClientRect()
        const pos = screenToFlowPosition({ x: rect.left + rect.width / 2, y: rect.top + rect.height / 2 })
        pasteNode(pos)
      }
    }
    window.addEventListener("keydown", handler)
    return () => window.removeEventListener("keydown", handler)
  }, [copyNode, pasteNode, screenToFlowPosition, t])

  return (
    <div style={{ display: "flex", flexDirection: "column", height: "100%" }}>
      <CanvasToolbar onExecute={onExecute} onCancel={onCancel} onDryRun={onDryRun} dryRunLoading={dryRunLoading} />
      <div ref={reactFlowWrapper} className="workflow-canvas">
        <ReactFlow
          nodes={nodes}
          edges={edges}
          nodesDraggable={!isExecuting && !reviewMode}
          nodesConnectable={!isExecuting && !reviewMode}
          elementsSelectable={!isExecuting && !reviewMode}
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

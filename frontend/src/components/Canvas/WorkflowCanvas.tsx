import { notifications } from "@mantine/notifications"
import { Background, BackgroundVariant, MiniMap, ReactFlow, useReactFlow, type Connection } from "@xyflow/react"
import "@xyflow/react/dist/style.css"
import { useCallback, useEffect, useMemo, useRef } from "react"
import { useTranslation } from "react-i18next"
import { useCanvasStore } from "./stores/canvasStore.ts"
import { ConnectedHandlesContext } from "./connectedHandlesContext.ts"
import styles from "./WorkflowCanvas.module.css"
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
  const nodesData = useCanvasStore((s) => s.nodes)
  const nodePositions = useCanvasStore((s) => s.nodePositions)
  const edges = useCanvasStore((s) => s.edges)
  const onNodesChange = useCanvasStore((s) => s.onNodesChange)
  const onEdgesChange = useCanvasStore((s) => s.onEdgesChange)
  const addEdge = useCanvasStore((s) => s.addEdge)
  const addNode = useCanvasStore((s) => s.addNode)
  const setSelectedNode = useCanvasStore((s) => s.setSelectedNode)
  const isExecuting = useCanvasStore((s) => s.isExecuting)
  const reviewMode = useCanvasStore((s) => s.reviewMode)
  const copyNode = useCanvasStore((s) => s.copyNode)
  const pasteNode = useCanvasStore((s) => s.pasteNode)

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

  // 一次性由 edges 计算每个节点已连接的 handle 集合（O(E)），通过 Context 下发，
  // 避免每个 CustomNode 各自执行 O(N×E) 的 edges.filter。
  const connectedHandlesByNode = useMemo(() => {
    const map: Record<string, Set<string>> = {}
    for (const e of edges) {
      if (e.source && e.sourceHandle) {
        (map[e.source] ??= new Set()).add(e.sourceHandle)
      }
      if (e.target && e.targetHandle) {
        (map[e.target] ??= new Set()).add(e.targetHandle)
      }
    }
    return map
  }, [edges])

  const onConnect = useCallback(
    (params: Connection) => {
      const { source, sourceHandle, target } = params
      let { targetHandle } = params

      if (source === target) {
        notifications.show({
          title: t('canvas.connectionRejected'),
          message: t('canvas.nodeSelfConnect'),
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
            title: t('canvas.connectionRejected'),
            message: t('canvas.sourcePortOutput'),
            color: "red",
          })
          return
        }
      }
      if (targetNode && targetHandle) {
        const port = targetNode.data.descriptor.ports.find((p) => `port-${p.name}` === targetHandle)
        if (port && port.direction !== "Input") {
          notifications.show({
            title: t('canvas.connectionRejected'),
            message: t('canvas.targetPortInput'),
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
            title: t('canvas.connectionRejected'),
            message: t('canvas.portTypeMismatch', { sourceType: sourcePort.type, targetType: targetPort.type }),
            color: "red",
          })
          return
        }
      }

      if (targetPort && targetHandle) {
        const maxConnections: Record<string, number> = { LLM: 1, Memory: 1 }
        const max = maxConnections[targetPort.type]
        if (max !== undefined) {
          const existingCount = useCanvasStore.getState().edges.filter(
            (e) => e.target === target && e.targetHandle === targetHandle,
          ).length
          if (existingCount >= max) {
            notifications.show({
              title: t('canvas.connectionRejected'),
              message: t('canvas.portMaxConnections', { displayName: targetPort.displayName, max }),
              color: "red",
            })
            return
          }
        }
      }

      const isDuplicate = useCanvasStore.getState().edges.some(
        (e) =>
          e.source === source &&
          e.sourceHandle === sourceHandle &&
          e.target === target &&
          e.targetHandle === targetHandle,
      )
      if (isDuplicate) {
        notifications.show({
          title: t('canvas.connectionRejected'),
          message: t('canvas.connectionExists'),
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
      const selectedId = useCanvasStore.getState().selectedNodeId
      if (e.key.toLowerCase() === "c" && selectedId) {
        e.preventDefault()
        copyNode(selectedId)
        notifications.show({ title: t('copied', { ns: 'common' }), message: t('list.nodeCopied'), color: "teal" })
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
    <div className={styles.root}>
      <CanvasToolbar onExecute={onExecute} onCancel={onCancel} onDryRun={onDryRun} dryRunLoading={dryRunLoading} />
      <div ref={reactFlowWrapper} className="workflow-canvas" data-testid="workflow-canvas">
        <ConnectedHandlesContext.Provider value={connectedHandlesByNode}>
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
        </ConnectedHandlesContext.Provider>
      </div>
    </div>
  )
}

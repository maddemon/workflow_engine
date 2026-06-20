import { ReactFlowProvider } from "@xyflow/react"
import { useEffect, useMemo, useRef } from "react"
import { useParams } from "react-router-dom"
import { WorkflowCanvas } from "../components/Canvas/WorkflowCanvas.tsx"
import { ExecutionPanel } from "../components/ExecutionPanel/ExecutionPanel.tsx"
import { NodePanel } from "../components/NodePanel/NodePanel.tsx"
import { ParameterPanel } from "../components/ParameterPanel/ParameterPanel.tsx"
import { useExecution } from "../hooks/useExecution.ts"
import { useNodeTypes } from "../hooks/useNodeTypes.ts"
import { useWorkflowStore } from "../stores/workflowStore.ts"

interface WorkflowEditorPageProps {
  onLayoutChange?: (navbar: React.ReactNode | null, aside: React.ReactNode | null) => void
}

export function WorkflowEditorPage({ onLayoutChange }: WorkflowEditorPageProps) {
  const { id } = useParams<{ id: string }>()
  useNodeTypes()
  const { execution, clearExecution, execute, error } = useExecution()
  const loadWorkflow = useWorkflowStore((s) => s.loadWorkflow)
  const newWorkflow = useWorkflowStore((s) => s.newWorkflow)
  const nodes = useWorkflowStore((s) => s.nodes)

  const nodeNames = useMemo(() => {
    const map: Record<string, string> = {}
    for (const node of nodes) {
      map[node.id] = node.data.name
    }
    return map
  }, [nodes])

  useEffect(() => {
    if (id && id !== "new") {
      loadWorkflow(id)
    } else {
      newWorkflow()
    }
  }, [id, loadWorkflow, newWorkflow])

  const navbar = useMemo(() => <NodePanel />, [])

  const aside = useMemo(() => {
    if (execution || error) {
      return <ExecutionPanel execution={execution} onClose={clearExecution} error={error} nodeNames={nodeNames} />
    }
    return <ParameterPanel />
  }, [execution, clearExecution, error, nodeNames])

  const asideKey = execution ? `${execution.id}-${execution.status}-${execution.completedAt ?? ''}` : (error ? "error" : "default")
  const prevKeyRef = useRef<string>(asideKey)

  useEffect(() => {
    if (prevKeyRef.current !== asideKey) {
      prevKeyRef.current = asideKey
      onLayoutChange?.(navbar, aside)
    }
  }, [asideKey, navbar, aside, onLayoutChange])

  useEffect(() => {
    onLayoutChange?.(navbar, aside)
    return () => onLayoutChange?.(null, null)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  return (
    <ReactFlowProvider>
      <WorkflowCanvas onExecute={execute} />
    </ReactFlowProvider>
  )
}

import { ReactFlowProvider } from "@xyflow/react"
import { useCallback, useEffect, useMemo } from "react"
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
  const { ready } = useNodeTypes()
  const { execution, clearExecution, execute, dryRun, dryRunLoading, cancelExecution, error } = useExecution()
  const loadWorkflow = useWorkflowStore((s) => s.loadWorkflow)
  const newWorkflow = useWorkflowStore((s) => s.newWorkflow)

  useEffect(() => {
    if (!ready) return
    clearExecution()
    if (id && id !== "new") {
      loadWorkflow(id)
    } else {
      newWorkflow()
    }
  }, [id, ready, clearExecution, loadWorkflow, newWorkflow])

  const navbar = useMemo(() => <NodePanel />, [])

  const aside = useMemo(() => {
    if (execution || error) {
      return <ExecutionPanel execution={execution} onClose={clearExecution} onCancel={cancelExecution} error={error} />
    }
    return <ParameterPanel />
  }, [execution, clearExecution, cancelExecution, error])

  const handleLayoutChange = useCallback(() => {
    onLayoutChange?.(navbar, aside)
  }, [onLayoutChange, navbar, aside])

  useEffect(() => {
    handleLayoutChange()
    return () => onLayoutChange?.(null, null)
  }, [handleLayoutChange, onLayoutChange])

  return (
    <ReactFlowProvider>
      <WorkflowCanvas onExecute={execute} onCancel={cancelExecution} onDryRun={dryRun} dryRunLoading={dryRunLoading} />
    </ReactFlowProvider>
  )
}

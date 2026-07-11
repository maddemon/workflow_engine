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
  const { execution, clearExecution, execute, dryRun, dryRunLoading, cancelExecution, error } = useExecution()
  const loadWorkflow = useWorkflowStore((s) => s.loadWorkflow)
  const newWorkflow = useWorkflowStore((s) => s.newWorkflow)

  // 使用 useRef 存储 clearExecution，避免依赖变化触发 useEffect
  const clearExecutionRef = useRef(clearExecution)
  clearExecutionRef.current = clearExecution

  useEffect(() => {
    // 切换工作流时清除旧的执行状态
    clearExecutionRef.current()
    if (id && id !== "new") {
      loadWorkflow(id)
    } else {
      newWorkflow()
    }
  }, [id, loadWorkflow, newWorkflow])

  const navbar = useMemo(() => <NodePanel />, [])

  const aside = useMemo(() => {
    if (execution || error) {
      return <ExecutionPanel execution={execution} onClose={clearExecution} onCancel={cancelExecution} error={error} />
    }
    return <ParameterPanel />
  }, [execution, clearExecution, cancelExecution, error])

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
      <WorkflowCanvas onExecute={execute} onCancel={cancelExecution} onDryRun={dryRun} dryRunLoading={dryRunLoading} />
    </ReactFlowProvider>
  )
}

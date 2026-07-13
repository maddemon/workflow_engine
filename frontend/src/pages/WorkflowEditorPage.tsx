import { ReactFlowProvider } from "@xyflow/react"
import { useCallback, useEffect, useMemo, useState } from "react"
import { useParams } from "react-router-dom"
import { WorkflowCanvas } from "../components/Canvas/WorkflowCanvas.tsx"
import { ExecutionPanel } from "../components/ExecutionPanel/ExecutionPanel.tsx"
import { NodePanel } from "../components/NodePanel/NodePanel.tsx"
import { ParameterPanel } from "../components/ParameterPanel/ParameterPanel.tsx"
import { useExecution } from "../hooks/useExecution.ts"
import { useNodeTypes } from "../hooks/useNodeTypes.ts"
import { useWorkflowStore } from "../stores/workflowStore.ts"
import { DiffPanel } from "../components/ParameterPanel/DiffPanel.tsx"
import { ValidationChecklistModal } from "../components/ParameterPanel/ValidationChecklistModal.tsx"
import { Stack, Text, Badge, Group, Button, Divider, Modal, Textarea } from "@mantine/core"
import { notifications } from "@mantine/notifications"
import { Eye, Play, Check, X } from "lucide-react"
import { confirmWorkflow, rejectDraft } from "../services/api.ts"

interface WorkflowEditorPageProps {
  onLayoutChange?: (navbar: React.ReactNode | null, aside: React.ReactNode | null) => void
}

export function WorkflowEditorPage({ onLayoutChange }: WorkflowEditorPageProps) {
  const { id } = useParams<{ id: string }>()
  const { ready } = useNodeTypes()
  const { execution, clearExecution, execute, dryRun, dryRunLoading, cancelExecution, error } = useExecution()
  const loadWorkflow = useWorkflowStore((s) => s.loadWorkflow)
  const newWorkflow = useWorkflowStore((s) => s.newWorkflow)
  const reviewMode = useWorkflowStore((s) => s.reviewMode)
  const setReviewMode = useWorkflowStore((s) => s.setReviewMode)
  const structuredDiff = useWorkflowStore((s) => s.structuredDiff)
  const workflowId = useWorkflowStore((s) => s.workflowId)

  const [highlightedNodeIds, setHighlightedNodeIds] = useState<string[]>([])
  const [validationModalOpen, setValidationModalOpen] = useState(false)
  const [rejectModalOpen, setRejectModalOpen] = useState(false)
  const [rejectReason, setRejectReason] = useState("")

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

    // In review mode, show a review panel
    if (reviewMode) {
      return (
        <Stack gap="xs" p="sm" style={{ height: '100%', overflow: 'hidden' }}>
          <Group gap={4}>
            <Eye size={14} />
            <Text fw={600} size="sm">Review Mode</Text>
            <Badge size="xs" color="blue" variant="light">AI Draft</Badge>
          </Group>
          <Text size="xs" c="dimmed">Review the AI-generated workflow below.</Text>

          {structuredDiff && structuredDiff.length > 0 && (
            <DiffPanel diff={structuredDiff} highlightedNodeIds={highlightedNodeIds} onNodeHighlight={setHighlightedNodeIds} />
          )}

          <ParameterPanel />

          <Divider />

          <Stack gap="xs">
            <Text fw={600} size="xs" tt="uppercase">Actions</Text>
            <Button leftSection={<Play size={14} />} variant="light" onClick={() => dryRun()} loading={dryRunLoading}>
              Dry Run
            </Button>
            <Button leftSection={<Check size={14} />} color="green" onClick={() => setValidationModalOpen(true)}>
              Confirm & Activate
            </Button>
            <Button leftSection={<X size={14} />} color="red" variant="light" onClick={() => setRejectModalOpen(true)}>
              Reject
            </Button>
            <Button variant="subtle" color="gray" onClick={() => setReviewMode(false)}>
              Switch to Manual Mode
            </Button>
          </Stack>
        </Stack>
      );
    }

    return <ParameterPanel />
  }, [execution, clearExecution, cancelExecution, error, reviewMode, structuredDiff, highlightedNodeIds, dryRun, dryRunLoading])

  const handleLayoutChange = useCallback(() => {
    onLayoutChange?.(navbar, aside)
  }, [onLayoutChange, navbar, aside])

  const handleConfirm = useCallback(async () => {
    if (!workflowId) return;
    try {
      await confirmWorkflow(workflowId);
      notifications.show({ title: 'Activated', message: 'Workflow confirmed and activated.', color: 'green' });
      setValidationModalOpen(false);
    } catch (err) {
      notifications.show({ title: 'Failed', message: err instanceof Error ? err.message : 'Confirmation failed', color: 'red' });
    }
  }, [workflowId]);

  const handleReject = useCallback(async () => {
    if (!workflowId || !rejectReason.trim()) return;
    try {
      await rejectDraft(workflowId, rejectReason);
      notifications.show({ title: 'Rejected', message: 'Draft rejected. Feedback sent to AI.', color: 'orange' });
      setRejectModalOpen(false);
      setRejectReason('');
    } catch (err) {
      notifications.show({ title: 'Failed', message: err instanceof Error ? err.message : 'Rejection failed', color: 'red' });
    }
  }, [workflowId, rejectReason]);

  useEffect(() => {
    handleLayoutChange()
    return () => onLayoutChange?.(null, null)
  }, [handleLayoutChange, onLayoutChange])

  return (
    <ReactFlowProvider>
      <WorkflowCanvas onExecute={execute} onCancel={cancelExecution} onDryRun={dryRun} dryRunLoading={dryRunLoading} />

      <ValidationChecklistModal
        opened={validationModalOpen}
        onClose={() => setValidationModalOpen(false)}
        onProceed={handleConfirm}
      />

      <Modal opened={rejectModalOpen} onClose={() => setRejectModalOpen(false)} title="Reject Draft" centered>
        <Stack gap="md">
          <Textarea
            label="Rejection Reason"
            placeholder="Describe what needs to be improved..."
            value={rejectReason}
            onChange={(e) => setRejectReason(e.target.value)}
            minRows={3}
          />
          <Group justify="flex-end">
            <Button variant="subtle" color="gray" onClick={() => setRejectModalOpen(false)}>Cancel</Button>
            <Button color="red" onClick={handleReject} disabled={!rejectReason.trim()}>Submit Rejection</Button>
          </Group>
        </Stack>
      </Modal>
    </ReactFlowProvider>
  )
}

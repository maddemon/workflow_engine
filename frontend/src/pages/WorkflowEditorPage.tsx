import { ReactFlowProvider } from "@xyflow/react"
import { useCallback, useEffect, useMemo, useState } from "react"
import { useParams } from "react-router-dom"
import { useTranslation } from "react-i18next"
import { WorkflowCanvas } from "../components/Canvas/WorkflowCanvas.tsx"
import { ExecutionPanel } from "../components/ExecutionPanel/ExecutionPanel.tsx"
import { NodePanel } from "../components/NodePanel/NodePanel.tsx"
import { ParameterPanel } from "../components/ParameterPanel/ParameterPanel.tsx"
import { useExecution } from "../hooks/useExecution.ts"
import { useNodeTypes } from "../hooks/useNodeTypes.ts"
import { useWorkflowStore } from "../stores/workflowStore.ts"
import { DiffPanel } from "../components/ParameterPanel/DiffPanel.tsx"
import { ValidationChecklistModal } from "../components/ParameterPanel/ValidationChecklistModal.tsx"
import { Alert, Anchor, Stack, Text, Badge, Group, Button, Divider, Modal, Textarea } from "@mantine/core"
import { notifications } from "@mantine/notifications"
import { Eye, Play, Check, RefreshCw, X } from "lucide-react"
import { confirmWorkflow, rejectDraft } from "../services/api.ts"
import { useWorkflowVersionPolling } from "../hooks/useWorkflowVersionPolling.ts"

interface WorkflowEditorPageProps {
  onLayoutChange?: (navbar: React.ReactNode | null, aside: React.ReactNode | null) => void
}

export function WorkflowEditorPage({ onLayoutChange }: WorkflowEditorPageProps) {
  const { t } = useTranslation(['workflow', 'common'])
  const { id } = useParams<{ id: string }>()
  const { ready } = useNodeTypes()
  const { execution, clearExecution, execute, dryRun, dryRunLoading, cancelExecution, error } = useExecution()
  const loadWorkflow = useWorkflowStore((s) => s.loadWorkflow)
  const newWorkflow = useWorkflowStore((s) => s.newWorkflow)
  const reviewMode = useWorkflowStore((s) => s.reviewMode)
  const setReviewMode = useWorkflowStore((s) => s.setReviewMode)
  const structuredDiff = useWorkflowStore((s) => s.structuredDiff)
  const workflowId = useWorkflowStore((s) => s.workflowId)
  const workflowVersion = useWorkflowStore((s) => s.workflowVersion)

  const [highlightedNodeIds, setHighlightedNodeIds] = useState<string[]>([])
  const [validationModalOpen, setValidationModalOpen] = useState(false)
  const [rejectModalOpen, setRejectModalOpen] = useState(false)
  const [rejectReason, setRejectReason] = useState("")
  const { changed, newVersion, dismiss } = useWorkflowVersionPolling(workflowId)

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
            <Text fw={600} size="sm">{t('editorReviewMode')}</Text>
            <Badge size="xs" color="blue" variant="light">{t('editorAiDraft')}</Badge>
          </Group>
          <Text size="xs" c="dimmed">{t('editorReviewHint')}</Text>

          {structuredDiff && structuredDiff.length > 0 && (
            <DiffPanel diff={structuredDiff} highlightedNodeIds={highlightedNodeIds} onNodeHighlight={setHighlightedNodeIds} />
          )}

          <ParameterPanel />

          <Divider />

          <Stack gap="xs">
            <Text fw={600} size="xs" tt="uppercase">{t('editorActions')}</Text>
            <Button leftSection={<Play size={14} />} variant="light" onClick={() => dryRun()} loading={dryRunLoading}>
              {t('editorDryRun')}
            </Button>
            <Button leftSection={<Check size={14} />} color="green" onClick={() => setValidationModalOpen(true)}>
              {t('editorConfirmActivate')}
            </Button>
            <Button leftSection={<X size={14} />} color="red" variant="light" onClick={() => setRejectModalOpen(true)}>
              {t('editorReject')}
            </Button>
            <Button variant="subtle" color="gray" onClick={() => setReviewMode(false)}>
              {t('editorSwitchManual')}
            </Button>
          </Stack>
        </Stack>
      );
    }

    return <ParameterPanel />
  }, [execution, clearExecution, cancelExecution, error, reviewMode, setReviewMode, structuredDiff, highlightedNodeIds, dryRun, dryRunLoading, t])

  const handleLayoutChange = useCallback(() => {
    onLayoutChange?.(navbar, aside)
  }, [onLayoutChange, navbar, aside])

  const handleConfirm = useCallback(async () => {
    if (!workflowId) return;
    try {
      await confirmWorkflow(workflowId);
      notifications.show({ title: t('editorActivated'), message: t('editorActivationMessage'), color: 'green' });
      setValidationModalOpen(false);
    } catch (err) {
      notifications.show({ title: t('error', { ns: 'common' }), message: err instanceof Error ? err.message : t('editorConfirmationFailed'), color: 'red' });
    }
  }, [workflowId, t]);

  const handleReject = useCallback(async () => {
    if (!workflowId || !rejectReason.trim()) return;
    try {
      await rejectDraft(workflowId, rejectReason);
      notifications.show({ title: t('editorRejected'), message: t('editorRejectionMessage'), color: 'orange' });
      setRejectModalOpen(false);
      setRejectReason('');
    } catch (err) {
      notifications.show({ title: t('error', { ns: 'common' }), message: err instanceof Error ? err.message : t('editorRejectionFailed'), color: 'red' });
    }
  }, [workflowId, rejectReason, t]);

  useEffect(() => {
    handleLayoutChange()
    return () => onLayoutChange?.(null, null)
  }, [handleLayoutChange, onLayoutChange])

  return (
    <>
      {changed && (
        <Alert
          icon={<RefreshCw size={16} />}
          color="blue"
          variant="light"
          withCloseButton
          onClose={dismiss}
          style={{ margin: 8 }}
        >
          {t('editorExternalChange', { oldVersion: workflowVersion ?? '?', newVersion: newVersion ?? '?' })}
          <Anchor
            component="button"
            ml="xs"
            onClick={() => {
              const store = useWorkflowStore.getState();
              if (store.isDirty) {
                if (!window.confirm(t('editorUnsavedChangesConfirm'))) return;
              }
              if (workflowId) {
                loadWorkflow(workflowId);
                dismiss();
              }
            }}
          >
            {t('editorLoadNewVersion')}
          </Anchor>
        </Alert>
      )}
    <ReactFlowProvider>
      <WorkflowCanvas onExecute={execute} onCancel={cancelExecution} onDryRun={dryRun} dryRunLoading={dryRunLoading} />

      <ValidationChecklistModal
        opened={validationModalOpen}
        onClose={() => setValidationModalOpen(false)}
        onProceed={handleConfirm}
      />

      <Modal opened={rejectModalOpen} onClose={() => setRejectModalOpen(false)} title={t('editorRejectDraftTitle')} centered>
        <Stack gap="md">
          <Textarea
            label={t('editorRejectionReason')}
            placeholder={t('editorRejectionReasonPlaceholder')}
            value={rejectReason}
            onChange={(e) => setRejectReason(e.target.value)}
            minRows={3}
          />
          <Group justify="flex-end">
            <Button variant="subtle" color="gray" onClick={() => setRejectModalOpen(false)}>{t('cancel', { ns: 'common' })}</Button>
            <Button color="red" onClick={handleReject} disabled={!rejectReason.trim()}>{t('editorSubmitRejection')}</Button>
          </Group>
        </Stack>
      </Modal>
    </ReactFlowProvider>
    </>
  )
}

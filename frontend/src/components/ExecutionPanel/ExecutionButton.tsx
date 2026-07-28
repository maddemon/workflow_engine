import { Button, Tooltip } from '@mantine/core';
import { Play } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { useWorkflowStore } from '../../stores/workflowStore.ts';
import { useCanvasStore } from '../Canvas/stores/canvasStore.ts';
import { useExecution } from '../../hooks/useExecution.ts';
import { useIsDirty } from '../../hooks/useIsDirty.ts';

export function ExecutionButton() {
  const { t } = useTranslation('execution');
  const workflowId = useWorkflowStore((s) => s.workflowId);
  const nodes = useCanvasStore((s) => s.nodes);
  const isDirty = useIsDirty();
  const { execute, status } = useExecution();

  const loading = status === 'loading';
  const disabled = !workflowId || nodes.length === 0 || loading || isDirty;

  const tooltipLabel = isDirty
    ? t('button.saveBeforeExecute')
    : !workflowId
      ? t('button.noWorkflowSelected')
      : nodes.length === 0
        ? t('button.addAtLeastOneNode')
        : '';

  const button = (
    <Button
      color="green"
      leftSection={<Play size={12} />}
      onClick={() => workflowId && execute(workflowId)}
      disabled={disabled}
      loading={loading}
      size="compact-xs"
    >
      {loading ? t('running') : t('execute')}
    </Button>
  );

  return tooltipLabel ? (
    <Tooltip label={tooltipLabel} position="bottom">
      {button}
    </Tooltip>
  ) : (
    button
  );
}

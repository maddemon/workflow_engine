import { Button, Tooltip } from '@mantine/core';
import { Play } from 'lucide-react';
import { useWorkflowStore } from '../../stores/workflowStore.ts';
import { useExecution } from '../../hooks/useExecution.ts';

export function ExecutionButton() {
  const workflowId = useWorkflowStore((s) => s.workflowId);
  const nodes = useWorkflowStore((s) => s.nodes);
  const isDirty = useWorkflowStore((s) => s.isDirty);
  const { execute, status } = useExecution();

  const loading = status === 'loading';
  const disabled = !workflowId || nodes.length === 0 || loading || isDirty;

  const tooltipLabel = isDirty
    ? 'Save workflow before executing'
    : !workflowId
      ? 'No workflow selected'
      : nodes.length === 0
        ? 'Add at least one node'
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
      {loading ? 'Running...' : 'Execute'}
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

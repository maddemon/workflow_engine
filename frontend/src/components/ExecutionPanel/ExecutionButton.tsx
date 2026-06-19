import { Button } from '@mantine/core';
import { Play } from 'lucide-react';
import { useWorkflowStore } from '../../stores/workflowStore.ts';
import { useExecution } from '../../hooks/useExecution.ts';

export function ExecutionButton() {
  const workflowId = useWorkflowStore((s) => s.workflowId);
  const nodes = useWorkflowStore((s) => s.nodes);
  const { execute, status } = useExecution();

  const loading = status === 'loading';
  const disabled = !workflowId || nodes.length === 0 || loading;

  return (
    <Button
      color="green"
      leftSection={<Play size={16} />}
      onClick={() => workflowId && execute(workflowId)}
      disabled={disabled}
      loading={loading}
    >
      {loading ? 'Executing...' : 'Execute'}
    </Button>
  );
}

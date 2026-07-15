import { Modal, Stack, Text, Group, Badge, Button, Loader } from '@mantine/core';
import { Check, X, AlertCircle } from 'lucide-react';
import { useRequest } from 'ahooks';
import { validateWorkflow } from '../../services/api.ts';
import { useWorkflowStore } from '../../stores/workflowStore.ts';

interface IValidationChecklistModalProps {
  opened: boolean;
  onClose: () => void;
  onProceed: () => void;
}

export function ValidationChecklistModal({ opened, onClose, onProceed }: IValidationChecklistModalProps) {
  const workflowId = useWorkflowStore((s) => s.workflowId);

  const { data: result, loading, error } = useRequest(
    () => validateWorkflow(workflowId!),
    {
      manual: true,
      ready: opened && !!workflowId,
      refreshDeps: [workflowId],
    },
  );

  const handleClose = () => {
    onClose();
  };

  const errorMessage = error instanceof Error ? error.message : error ? String(error) : null;

  return (
    <Modal opened={opened} onClose={handleClose} title="Pre-flight Checklist" size="lg" centered>
      <Stack gap="md">
        <Text size="sm" c="dimmed">
          Validating workflow before activation. Check each item below.
        </Text>

        {loading && (
          <Group justify="center" py="md">
            <Loader size="sm" />
            <Text size="sm">Validating...</Text>
          </Group>
        )}

        {errorMessage && (
          <Group gap={4} c="red">
            <AlertCircle size={14} />
            <Text size="sm">{errorMessage}</Text>
          </Group>
        )}

        {result && (
          <Stack gap="xs">
            <Group gap={4}>
              {result.valid ? (
                <Check size={16} color="var(--mantine-color-green-text)" />
              ) : (
                <X size={16} color="var(--mantine-color-red-text)" />
              )}
              <Text fw={600} size="sm" c={result.valid ? 'green' : 'red'}>
                {result.valid ? 'All checks passed' : `${result.errors.length} issue(s) found`}
              </Text>
            </Group>

            {result.errors.map((err, idx) => (
              <Group key={idx} gap={4} align="flex-start" wrap="nowrap">
                <Badge size="xs" color="red" variant="light">{err.errorType}</Badge>
                <Stack gap={0}>
                  <Text size="xs">{err.message}</Text>
                  {err.nodeId && <Text size="xs" c="dimmed">Node: {err.nodeId}</Text>}
                  {err.suggestedFix && <Text size="xs" c="blue">{err.suggestedFix}</Text>}
                </Stack>
              </Group>
            ))}
          </Stack>
        )}

        <Group justify="flex-end">
          <Button variant="subtle" color="gray" onClick={handleClose}>Cancel</Button>
          <Button onClick={onProceed} disabled={!result?.valid}>
            Confirm & Activate
          </Button>
        </Group>
      </Stack>
    </Modal>
  );
}

import { useEffect, useState } from 'react';
import { Modal, Stack, Text, Group, Badge, Button, Loader } from '@mantine/core';
import { Check, X, AlertCircle } from 'lucide-react';
import { validateWorkflow } from '../../services/api.ts';
import { useWorkflowStore } from '../../stores/workflowStore.ts';
import type { ValidateWorkflowResult } from '../../types/workflow.ts';

interface IValidationChecklistModalProps {
  opened: boolean;
  onClose: () => void;
  onProceed: () => void;
}

export function ValidationChecklistModal({ opened, onClose, onProceed }: IValidationChecklistModalProps) {
  const workflowId = useWorkflowStore((s) => s.workflowId);
  const [result, setResult] = useState<ValidateWorkflowResult | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const runValidation = async () => {
    if (!workflowId) return;
    setLoading(true);
    setError(null);
    try {
      const res = await validateWorkflow(workflowId);
      setResult(res);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Validation failed');
    } finally {
      setLoading(false);
    }
  };

  // auto-run on open
  useEffect(() => {
    if (opened) runValidation();
  }, [opened]);

  const handleClose = () => {
    setResult(null);
    setError(null);
    onClose();
  };

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

        {error && (
          <Group gap={4} c="red">
            <AlertCircle size={14} />
            <Text size="sm">{error}</Text>
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

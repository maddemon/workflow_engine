import { Modal, Stack, Text, Group, Badge, Button, Loader } from '@mantine/core';
import { Check, X, AlertCircle } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { useRequest } from 'ahooks';
import { validateWorkflow } from '../../services/api.ts';
import { useWorkflowStore } from '../../stores/workflowStore.ts';

interface IValidationChecklistModalProps {
  opened: boolean;
  onClose: () => void;
  onProceed: () => void;
}

export function ValidationChecklistModal({ opened, onClose, onProceed }: IValidationChecklistModalProps) {
  const { t } = useTranslation('parameterPanel');
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
    <Modal opened={opened} onClose={handleClose} title={t('validationChecklistModal.title')} size="lg" centered>
      <Stack gap="md">
        <Text size="sm" c="dimmed">
          {t('validationChecklistModal.description')}
        </Text>

        {loading && (
          <Group justify="center" py="md">
            <Loader size="sm" />
            <Text size="sm">{t('validationChecklistModal.validating')}</Text>
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
                {result.valid ? t('validationChecklistModal.allChecksPassed') : t('validationChecklistModal.issuesFound', { count: result.errors.length })}
              </Text>
            </Group>

            {result.errors.map((err, idx) => (
              <Group key={idx} gap={4} align="flex-start" wrap="nowrap">
                <Badge size="xs" color="red" variant="light">{err.errorType}</Badge>
                <Stack gap={0}>
                  <Text size="xs">{err.message}</Text>
                  {err.nodeId && <Text size="xs" c="dimmed">{t('validationChecklistModal.node')}: {err.nodeId}</Text>}
                  {err.suggestedFix && <Text size="xs" c="blue">{err.suggestedFix}</Text>}
                </Stack>
              </Group>
            ))}
          </Stack>
        )}

        <Group justify="flex-end">
          <Button variant="subtle" color="gray" onClick={handleClose}>{t('validationChecklistModal.cancel')}</Button>
          <Button onClick={onProceed} disabled={!result?.valid}>
            {t('validationChecklistModal.confirmActivate')}
          </Button>
        </Group>
      </Stack>
    </Modal>
  );
}

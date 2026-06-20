import { Stack, Text, Group, ActionIcon, Divider, Box, Loader } from '@mantine/core';
import { X, AlertCircle } from 'lucide-react';
import type { ExecutionDto } from '../../types/workflow.ts';
import { NodeOutputList } from './NodeOutputList.tsx';
import { useWorkflowStore } from '../../stores/workflowStore.ts';

interface ExecutionPanelProps {
  execution: ExecutionDto | null;
  onClose: () => void;
  error?: string | null;
  nodeNames?: Record<string, string>;
}

export function ExecutionPanel({ execution, onClose, error, nodeNames }: ExecutionPanelProps) {
  const nodeExecutionRecords = useWorkflowStore((s) => s.nodeExecutionRecords);
  const records = Object.values(nodeExecutionRecords);
  if (!execution) {
    return error ? (
      <Stack gap="sm" p="sm">
        <Group justify="space-between" align="center">
          <Text fw={600} size="md">Execution Error</Text>
          <ActionIcon variant="subtle" onClick={onClose} aria-label="Close">
            <X size={16} />
          </ActionIcon>
        </Group>
        <Divider />
        <Box
          p="sm"
          style={{
            background: 'var(--exec-err-bg)',
            border: '1px solid var(--exec-err-border)',
            borderRadius: 6,
          }}
        >
          <Group gap={6} wrap="nowrap" align="flex-start">
            <AlertCircle size={16} color="var(--exec-err-color)" style={{ flexShrink: 0, marginTop: 1 }} />
            <Text size="sm" style={{ color: 'var(--exec-err-color)', lineHeight: 1.5, wordBreak: 'break-word' }}>{error}</Text>
          </Group>
        </Box>
      </Stack>
    ) : null;
  }

  const isRunning = execution.status === 'Pending' || execution.status === 'Running';

  return (
    <Stack gap="sm" p="sm">
      <Group justify="space-between" align="center">
        <Text fw={600} size="md">Execution Result</Text>
        <Group gap="xs" align="center" wrap="nowrap">
          {isRunning && <Loader size={14} />}
          <ActionIcon variant="subtle" onClick={onClose} aria-label="Close">
            <X size={16} />
          </ActionIcon>
        </Group>
      </Group>
      <Divider />

      {error && (
        <Box
          p="xs"
          style={{
            background: 'var(--exec-err-bg)',
            border: '1px solid var(--exec-err-border)',
            borderRadius: 6,
          }}
        >
          <Group gap={6} wrap="nowrap" align="flex-start">
            <AlertCircle size={14} color="var(--exec-err-color)" style={{ flexShrink: 0, marginTop: 1 }} />
            <Text size="xs" style={{ color: 'var(--exec-err-color)', lineHeight: 1.5, wordBreak: 'break-word' }}>{error}</Text>
          </Group>
        </Box>
      )}

      {isRunning && records.length === 0 && (
        <Text size="sm" c="dimmed" ta="center" py="md">
          Waiting for execution to start...
        </Text>
      )}

      <NodeOutputList records={records} nodeNames={nodeNames} />
    </Stack>
  );
}

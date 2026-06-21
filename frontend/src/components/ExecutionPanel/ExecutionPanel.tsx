import { Stack, Text, Group, ActionIcon, Divider, Box, Loader, Badge } from '@mantine/core';
import { X, AlertCircle, Check, Clock, Loader as LoaderIcon } from 'lucide-react';
import type { ExecutionDto } from '../../types/workflow.ts';
import { NodeOutputList } from './NodeOutputList.tsx';
import { useWorkflowStore } from '../../stores/workflowStore.ts';

interface ExecutionPanelProps {
  execution: ExecutionDto | null;
  onClose: () => void;
  error?: string | null;
  nodeNames?: Record<string, string>;
}

const statusConfig: Record<string, { color: string; icon: React.ReactNode; label: string }> = {
  Pending: { color: 'gray', icon: <Clock size={14} />, label: 'Pending' },
  Running: { color: 'blue', icon: <LoaderIcon size={14} speed={2} />, label: 'Running' },
  Completed: { color: 'green', icon: <Check size={14} strokeWidth={3} />, label: 'Completed' },
  Failed: { color: 'red', icon: <X size={14} strokeWidth={3} />, label: 'Failed' },
  Cancelled: { color: 'gray', icon: <X size={14} />, label: 'Cancelled' },
};

function formatDuration(startedAt: string | null, completedAt: string | null): string | null {
  if (!startedAt) return null;
  const start = new Date(startedAt).getTime();
  const end = completedAt ? new Date(completedAt).getTime() : Date.now();
  const ms = end - start;
  if (ms < 0) return null;
  if (ms < 1000) return `${ms}ms`;
  if (ms < 60000) return `${(ms / 1000).toFixed(1)}s`;
  const minutes = Math.floor(ms / 60000);
  const seconds = Math.floor((ms % 60000) / 1000);
  return `${minutes}m ${seconds}s`;
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
  const statusInfo = statusConfig[execution.status] ?? statusConfig.Pending;
  const duration = formatDuration(execution.startedAt, execution.completedAt);

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

      <Group gap="xs" wrap="nowrap">
        <Badge
          color={statusInfo.color}
          variant="light"
          size="sm"
          leftSection={statusInfo.icon}
        >
          {statusInfo.label}
        </Badge>
        {duration && (
          <Text size="xs" c="dimmed">
            {duration}
          </Text>
        )}
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

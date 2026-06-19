import { Stack, Text, Badge, Group, ActionIcon, Divider } from '@mantine/core';
import { X } from 'lucide-react';
import type { ExecutionDto, ExecutionStatus } from '../../types/workflow.ts';
import { NodeOutputList } from './NodeOutputList.tsx';

interface ExecutionPanelProps {
  execution: ExecutionDto | null;
  onClose: () => void;
}

const statusColors: Record<ExecutionStatus, string> = {
  Pending: 'orange',
  Running: 'blue',
  Completed: 'green',
  Failed: 'red',
  Cancelled: 'gray',
};

export function ExecutionPanel({ execution, onClose }: ExecutionPanelProps) {
  if (!execution) return null;

  return (
    <Stack gap="sm" p="sm">
      <Group justify="space-between" align="center">
        <Text fw={600} size="md">Execution Result</Text>
        <ActionIcon variant="subtle" onClick={onClose} aria-label="Close">
          <X size={16} />
        </ActionIcon>
      </Group>
      <Divider />
      <Group gap="sm" align="center">
        <Badge variant="light" color={statusColors[execution.status]}>
          {execution.status}
        </Badge>
        {execution.startedAt && (
          <Text size="xs" c="dimmed">
            {new Date(execution.startedAt).toLocaleTimeString()}
          </Text>
        )}
      </Group>
      <NodeOutputList records={execution.nodeRecords} />
    </Stack>
  );
}

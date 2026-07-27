import { useState, useEffect } from 'react';
import { Stack, Text, Group, ActionIcon, Divider, Box, Loader, Badge, Button, useMantineTheme } from '@mantine/core';
import { X, AlertCircle, Check, Clock, Loader as LoaderIcon, Square } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { useShallow } from 'zustand/shallow';
import type { ExecutionDto } from '../../types/workflow.ts';
import { NodeOutputList } from './NodeOutputList.tsx';
import { useCanvasStore } from '../Canvas/stores/canvasStore.ts';

interface ExecutionPanelProps {
  execution: ExecutionDto | null;
  onClose: () => void;
  onCancel?: () => Promise<void>;
  error?: string | null;
}

const statusConfig: Record<string, { color: string; icon: React.ReactNode; labelKey: string }> = {
  Pending: { color: 'gray', icon: <Clock size={14} />, labelKey: 'status.pending' },
  Running: { color: 'blue', icon: <LoaderIcon size={14} speed={2} />, labelKey: 'status.running' },
  Completed: { color: 'green', icon: <Check size={14} strokeWidth={3} />, labelKey: 'status.completed' },
  Failed: { color: 'red', icon: <X size={14} strokeWidth={3} />, labelKey: 'status.failed' },
  Cancelled: { color: 'gray', icon: <X size={14} />, labelKey: 'status.cancelled' },
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

function useLiveDuration(startedAt: string | null, completedAt: string | null): string | null {
  const [, setTick] = useState(0);
  const isRunning = startedAt && !completedAt;

  useEffect(() => {
    if (!isRunning) return;
    const interval = setInterval(() => setTick((t) => t + 1), 1000);
    return () => clearInterval(interval);
  }, [isRunning]);

  return formatDuration(startedAt, completedAt);
}

export function ExecutionPanel({ execution, onClose, onCancel, error }: ExecutionPanelProps) {
  const { t } = useTranslation('execution');
  const theme = useMantineTheme();
  const nodeExecutionRecords = useCanvasStore((s) => s.nodeExecutionRecords);
  const records = Object.values(nodeExecutionRecords);
  const [cancelling, setCancelling] = useState(false);

  const handleCancel = async () => {
    if (!execution) return;
    setCancelling(true);
    try {
      await onCancel?.();
    } finally {
      setCancelling(false);
    }
  };

  // 用稳定的派生选择器（useShallow 按 id→name 浅比较）计算 nodeNames/nodeTypeNames，
  // 执行状态 tick 仅改变 node.executionStatus，不影响 name/typeName 映射，
  // 因此不会触发本组件无谓重渲染（也不会在画布拖拽时重算）。
  const nodeNames = useCanvasStore(
    useShallow((s) => {
      const map: Record<string, string> = {};
      for (const n of s.nodes) map[n.id] = n.data.name;
      return map;
    }),
  );

  const nodeTypeNames = useCanvasStore(
    useShallow((s) => {
      const map: Record<string, string> = {};
      for (const n of s.nodes) map[n.id] = n.data.typeName;
      return map;
    }),
  );

  const isRunning = execution?.status === 'Pending' || execution?.status === 'Running';
  const statusInfo = statusConfig[execution?.status ?? ''] ?? statusConfig.Pending;
  const duration = useLiveDuration(execution?.startedAt ?? null, execution?.completedAt ?? null);

  if (!execution) {
    return error ? (
      <Stack gap="sm" p="sm">
        <Group justify="space-between" align="center">
          <Text fw={600} size="md">{t('executionError')}</Text>
          <ActionIcon variant="subtle" onClick={onClose} aria-label={t('common:close')}>
            <X size={16} />
          </ActionIcon>
        </Group>
        <Divider />
        <Box
          p="sm"
          style={{
            background: theme.other.execErrBg,
            border: `1px solid ${theme.other.execErrBorder}`,
            borderRadius: 6,
          }}
        >
          <Group gap={6} wrap="nowrap" align="flex-start">
            <AlertCircle size={16} color={theme.other.execErrColor} style={{ flexShrink: 0, marginTop: 1 }} />
            <Text size="sm" style={{ color: theme.other.execErrColor, lineHeight: 1.5, wordBreak: 'break-word' }}>{error}</Text>
          </Group>
        </Box>
      </Stack>
    ) : null;
  }

  return (
    <Stack gap="sm" p="sm">
      <Group justify="space-between" align="center">
        <Text fw={600} size="md">{t('executionResult')}</Text>
        <Group gap="xs" align="center" wrap="nowrap">
          {isRunning && <Loader size={14} />}
          {isRunning ? (
            <Button
              variant="subtle"
              size="compact-xs"
              color="red"
              leftSection={<Square size={12} />}
              onClick={handleCancel}
              loading={cancelling}
            >
              {t('stop')}
            </Button>
          ) : (
            <ActionIcon variant="subtle" onClick={onClose} aria-label={t('common:close')}>
              <X size={16} />
            </ActionIcon>
          )}
        </Group>
      </Group>

      <Group gap="xs" wrap="nowrap">
        <Badge
          color={statusInfo.color}
          variant="light"
          size="sm"
          leftSection={statusInfo.icon}
        >
          {t(statusInfo.labelKey)}
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
            background: theme.other.execErrBg,
            border: `1px solid ${theme.other.execErrBorder}`,
            borderRadius: 6,
          }}
        >
          <Group gap={6} wrap="nowrap" align="flex-start">
            <AlertCircle size={14} color={theme.other.execErrColor} style={{ flexShrink: 0, marginTop: 1 }} />
            <Text size="xs" style={{ color: theme.other.execErrColor, lineHeight: 1.5, wordBreak: 'break-word' }}>{error}</Text>
          </Group>
        </Box>
      )}

      {isRunning && records.length === 0 && (
        <Text size="sm" c="dimmed" ta="center" py="md">
          {t('waitingForStart')}
        </Text>
      )}

      <NodeOutputList records={records} nodeNames={nodeNames} nodeTypeNames={nodeTypeNames} />

      {isRunning && (
        <Button
          variant="outline"
          size="sm"
          color="red"
          leftSection={<Square size={14} />}
          onClick={handleCancel}
          loading={cancelling}
          fullWidth
        >
          {t('stopExecution')}
        </Button>
      )}
    </Stack>
  );
}

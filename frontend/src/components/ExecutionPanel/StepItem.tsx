import { Stack, Text, Box, Collapse, UnstyledButton, Group } from '@mantine/core';
import { Check, X, Clock, Loader, AlertCircle, ChevronRight, ChevronDown, FileText } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { CodeViewer } from './CodeViewer.tsx';
import { AgentExecutionView } from '../ExecutionView/AgentExecutionView.tsx';
import type { NodeExecutionRecordDto, ExecutionStatus } from '../../types/workflow.ts';
import type { AgentExecutionData } from '../../types/agent-execution.ts';
import { extractError, formatDuration, formatOutputSummary, isAgentOutput } from './nodeOutputUtils.ts';
import styles from './StepItem.module.css';

const statusConfig: Record<ExecutionStatus, { icon: React.ReactNode; shade: string; labelKey: string }> = {
  Pending: { icon: <Clock size={13} />, shade: 'gray', labelKey: 'status.pending' },
  Running: { icon: <Loader size={13} speed={2} />, shade: 'blue', labelKey: 'status.running' },
  Completed: { icon: <Check size={13} strokeWidth={3} />, shade: 'green', labelKey: 'status.completed' },
  Compensating: { icon: <Loader size={13} speed={2} />, shade: 'yellow', labelKey: 'status.running' },
  Compensated: { icon: <Check size={13} strokeWidth={3} />, shade: 'teal', labelKey: 'status.completed' },
  CompensationFailed: { icon: <X size={13} strokeWidth={3} />, shade: 'red', labelKey: 'status.failed' },
  DryRunCompleted: { icon: <Check size={13} strokeWidth={3} />, shade: 'green', labelKey: 'status.completed' },
  Failed: { icon: <X size={13} strokeWidth={3} />, shade: 'red', labelKey: 'status.failed' },
  Cancelled: { icon: <X size={13} />, shade: 'gray', labelKey: 'status.cancelled' },
};

export function StepItem({
  record,
  isLast,
  isExpanded,
  onToggle,
  nodeName,
  isAgent,
}: {
  record: NodeExecutionRecordDto;
  isLast: boolean;
  isExpanded: boolean;
  onToggle: () => void;
  nodeName?: string;
  isAgent?: boolean;
}) {
  const { t } = useTranslation('execution');
  const config = statusConfig[record.status] ?? statusConfig.Pending;
  const nodeError = record.status === 'Failed' ? extractError(record.output) : null;
  const duration = formatDuration(record.startedAt, record.completedAt);
  const outputSummary = record.output !== undefined && record.output !== null
    ? formatOutputSummary(record.output)
    : null;

  const agentData = (isAgent && isAgentOutput(record.output)) ? (record.output as AgentExecutionData) : null;

  const statusBg =
    record.status === 'Completed' ? 'var(--exec-success-bg)'
    : record.status === 'Failed' ? 'var(--exec-error-bg)'
    : record.status === 'Running' ? 'var(--exec-running-bg)'
    : 'var(--exec-pending-bg)';

  return (
    <div className={styles.row}>
      {/* Fixed icon + connector column */}
      <div className={styles.iconColumn}>
        <div
          className={styles.statusIcon}
          style={{
            background: statusBg,
            borderColor: `var(--mantine-color-${config.shade}-3)`,
            color: `var(--mantine-color-${config.shade}-6)`,
          }}
        >
          {config.icon}
        </div>
        {!isLast && (
          <div className={styles.connector} />
        )}
      </div>

      {/* Content column */}
      <div className={styles.content} style={{ paddingBottom: isLast ? 0 : 4 }}>
        <UnstyledButton
          w="100%"
          onClick={onToggle}
          className={styles.itemButton}
        >
          <Group gap="xs" wrap="nowrap">
            <Text size="sm" fw={500} flex={1} truncate>
              {nodeName || record.nodeDefinitionId.slice(0, 8)}
            </Text>
            {duration && (
              <Text size="xs" c="dimmed" style={{ flexShrink: 0, fontVariantNumeric: 'tabular-nums' }}>
                {duration}
              </Text>
            )}
            <div className={styles.chevron}>
              {isExpanded ? <ChevronDown size={14} /> : <ChevronRight size={14} />}
            </div>
          </Group>
        </UnstyledButton>

        {!isExpanded && nodeError && (
          <Box
            mt={4}
            mx={4}
            p="xs"
            style={{
              background: 'var(--exec-err-bg)',
              border: '1px solid var(--exec-err-border)',
              borderRadius: 4,
            }}
          >
            <Group gap={4} wrap="nowrap" align="flex-start">
              <AlertCircle size={12} color="var(--exec-err-color)" style={{ flexShrink: 0, marginTop: 1 }} />
              <Text
                size="xs"
                style={{
                  color: 'var(--exec-err-color)',
                  lineHeight: 1.4,
                  display: '-webkit-box',
                  WebkitLineClamp: 2,
                  WebkitBoxOrient: 'vertical',
                  overflow: 'hidden',
                }}
              >
                {nodeError.message}
              </Text>
            </Group>
          </Box>
        )}

        {!isExpanded && !nodeError && outputSummary && (
          <Box
            mt={4}
            mx={4}
            p="xs"
            style={{
              background: 'var(--exec-pending-bg)',
              border: '1px solid var(--exec-connector)',
              borderRadius: 4,
            }}
          >
            <Group gap={4} wrap="nowrap" align="flex-start">
              <FileText size={12} color="var(--mantine-color-dimmed)" style={{ flexShrink: 0, marginTop: 1 }} />
              <Text
                size="xs"
                c="dimmed"
                style={{
                  lineHeight: 1.4,
                  display: '-webkit-box',
                  WebkitLineClamp: 2,
                  WebkitBoxOrient: 'vertical',
                  overflow: 'hidden',
                }}
              >
                {outputSummary}
              </Text>
            </Group>
          </Box>
        )}

        <Collapse expanded={isExpanded}>
          <Stack gap={6} mt={4}>
            {agentData ? (
              <AgentExecutionView data={agentData} />
            ) : (
              <>
                {record.output !== undefined && record.output !== null && (
                  <CodeViewer
                    label={t('output')}
                    code={typeof record.output === 'string' ? record.output : JSON.stringify(record.output, null, 2)}
                    maxHeight={150}
                  />
                )}
                {record.resolvedParameters && (
                  <CodeViewer
                    label={t('parameters')}
                    code={JSON.stringify(record.resolvedParameters, null, 2)}
                    maxHeight={100}
                  />
                )}
              </>
            )}
          </Stack>
        </Collapse>
      </div>
    </div>
  );
}

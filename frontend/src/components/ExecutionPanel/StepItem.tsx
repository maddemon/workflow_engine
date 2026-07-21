import { Stack, Text, Box, Collapse, UnstyledButton, Group } from '@mantine/core';
import { Check, X, Clock, Loader, AlertCircle, ChevronRight, ChevronDown, FileText } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { CodeViewer } from './CodeViewer.tsx';
import { AgentExecutionView } from '../ExecutionView/AgentExecutionView.tsx';
import type { NodeExecutionRecordDto, ExecutionStatus } from '../../types/workflow.ts';
import type { AgentExecutionData } from '../../types/agent-execution.ts';
import { extractError, formatDuration, formatOutputSummary, isAgentOutput } from './nodeOutputUtils.ts';

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
    <div style={{ display: 'flex', gap: 10, position: 'relative' }}>
      {/* Fixed icon + connector column */}
      <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', flexShrink: 0 }}>
        <div
          style={{
            width: 26,
            height: 26,
            borderRadius: '50%',
            background: statusBg,
            border: `1.5px solid var(--mantine-color-${config.shade}-3)`,
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            color: `var(--mantine-color-${config.shade}-6)`,
            flexShrink: 0,
          }}
        >
          {config.icon}
        </div>
        {!isLast && (
          <div
            style={{
              width: 1.5,
              flex: 1,
              minHeight: 12,
              background: 'var(--exec-connector)',
              borderRadius: 1,
            }}
          />
        )}
      </div>

      {/* Content column */}
      <div style={{ flex: 1, minWidth: 0, paddingBottom: isLast ? 0 : 4 }}>
        <UnstyledButton
          w="100%"
          onClick={onToggle}
          style={{
            borderRadius: 6,
            padding: '6px 8px',
            transition: 'background 0.15s ease',
          }}
          onMouseEnter={(e) => {
            (e.currentTarget as HTMLElement).style.background = 'var(--exec-hover)';
          }}
          onMouseLeave={(e) => {
            (e.currentTarget as HTMLElement).style.background = 'transparent';
          }}
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
            <div style={{ color: 'var(--mantine-color-dimmed)', flexShrink: 0 }}>
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

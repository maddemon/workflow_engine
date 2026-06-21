import { useState } from 'react';
import { Stack, Text, Box, Collapse, UnstyledButton, Group } from '@mantine/core';
import { Check, X, Clock, Loader, AlertCircle, ChevronRight, ChevronDown, FileText } from 'lucide-react';
import { CodeViewer } from './CodeViewer.tsx';
import type { NodeExecutionRecordDto, ExecutionStatus } from '../../types/workflow.ts';

interface NodeOutputListProps {
  records: NodeExecutionRecordDto[];
  nodeNames?: Record<string, string>;
}

const statusConfig: Record<ExecutionStatus, { icon: React.ReactNode; shade: string; label: string }> = {
  Pending: { icon: <Clock size={13} />, shade: 'gray', label: 'Pending' },
  Running: { icon: <Loader size={13} speed={2} />, shade: 'blue', label: 'Running' },
  Completed: { icon: <Check size={13} strokeWidth={3} />, shade: 'green', label: 'Completed' },
  Failed: { icon: <X size={13} strokeWidth={3} />, shade: 'red', label: 'Failed' },
  Cancelled: { icon: <X size={13} />, shade: 'gray', label: 'Cancelled' },
};

function extractError(output: unknown): { code?: string; message?: string } | null {
  if (!output || typeof output !== 'object') return null;
  const obj = output as Record<string, unknown>;
  if (obj.error && typeof obj.error === 'object') {
    const err = obj.error as Record<string, unknown>;
    return { code: String(err.code ?? ''), message: String(err.message ?? '') };
  }
  if (obj.output && typeof obj.output === 'object') {
    const out = obj.output as Record<string, unknown>;
    const items = out.items;
    if (Array.isArray(items) && items.length > 0) {
      const first = items[0] as Record<string, unknown>;
      if (first.error && typeof first.error === 'object') {
        const err = first.error as Record<string, unknown>;
        return { code: String(err.code ?? ''), message: String(err.message ?? '') };
      }
      if (first.success === false && first.error) {
        const err = first.error as Record<string, unknown>;
        return { code: String(err.code ?? ''), message: String(err.message ?? '') };
      }
      if (first.success === false && !first.error) {
        return { message: 'Node execution failed.' };
      }
    }
  }
  return null;
}

function formatDuration(startedAt: string | null, completedAt: string | null): string | null {
  if (!startedAt) return null;
  const start = new Date(startedAt).getTime();
  const end = completedAt ? new Date(completedAt).getTime() : Date.now();
  const ms = end - start;
  if (ms < 0) return null;
  if (ms < 1000) return `${ms}ms`;
  return `${(ms / 1000).toFixed(1)}s`;
}

function formatOutputSummary(output: unknown): string {
  if (output === null || output === undefined) return 'No output';
  if (typeof output === 'string') return output.length > 100 ? `${output.slice(0, 100)}...` : output;
  if (typeof output === 'number' || typeof output === 'boolean') return String(output);

  const str = JSON.stringify(output, null, 2);
  if (str.length > 200) {
    try {
      const parsed = JSON.parse(str);
      if (Array.isArray(parsed)) {
        return `Array(${parsed.length} items)`;
      }
      if (typeof parsed === 'object' && parsed !== null) {
        const keys = Object.keys(parsed);
        return `Object{${keys.slice(0, 3).join(', ')}${keys.length > 3 ? ', ...' : ''}}`;
      }
    } catch {
      return `${str.slice(0, 200)}...`;
    }
  }
  return str;
}

function StepItem({
  record,
  isLast,
  isExpanded,
  onToggle,
  nodeName,
}: {
  record: NodeExecutionRecordDto;
  isLast: boolean;
  isExpanded: boolean;
  onToggle: () => void;
  nodeName?: string;
}) {
  const config = statusConfig[record.status] ?? statusConfig.Pending;
  const nodeError = record.status === 'Failed' ? extractError(record.output) : null;
  const duration = formatDuration(record.startedAt, record.completedAt);
  const outputSummary = record.output !== undefined && record.output !== null
    ? formatOutputSummary(record.output)
    : null;

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
            {record.output !== undefined && record.output !== null && (
              <CodeViewer
                label="Output"
                code={typeof record.output === 'string' ? record.output : JSON.stringify(record.output, null, 2)}
                maxHeight={150}
              />
            )}
            {record.resolvedParameters && (
              <CodeViewer
                label="Parameters"
                code={JSON.stringify(record.resolvedParameters, null, 2)}
                maxHeight={100}
              />
            )}
          </Stack>
        </Collapse>
      </div>
    </div>
  );
}

export function NodeOutputList({ records, nodeNames }: NodeOutputListProps) {
  const [expanded, setExpanded] = useState<Record<string, boolean>>({});

  const toggle = (id: string) => {
    setExpanded((prev) => ({ ...prev, [id]: !prev[id] }));
  };

  if (records.length === 0) {
    return (
      <Text size="sm" c="dimmed" ta="center" py="md">
        No node records
      </Text>
    );
  }

  return (
    <div style={{ display: 'flex', flexDirection: 'column' }}>
      {records.map((record, index) => (
        <StepItem
          key={record.id}
          record={record}
          isLast={index === records.length - 1}
          isExpanded={!!expanded[record.id]}
          onToggle={() => toggle(record.id)}
          nodeName={nodeNames?.[record.nodeDefinitionId]}
        />
      ))}
    </div>
  );
}

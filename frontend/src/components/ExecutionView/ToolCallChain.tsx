import { useState } from 'react';
import { Stack, Text, Box, Collapse, UnstyledButton, Group, Badge } from '@mantine/core';
import {
  Check,
  X,
  Clock,
  Loader,
  ChevronRight,
  ChevronDown,
  Wrench,
  AlertCircle,
} from 'lucide-react';
import { CodeViewer } from '../ExecutionPanel/CodeViewer.tsx';
import type { ToolCallRecord, ExecutionStatus } from '../../types/agent-execution.ts';

interface ToolCallChainProps {
  toolCalls: ToolCallRecord[];
}

const statusConfig: Record<ExecutionStatus, { color: string; icon: React.ReactNode; label: string }> = {
  Pending: { color: 'gray', icon: <Clock size={12} />, label: 'Pending' },
  Running: { color: 'blue', icon: <Loader size={12} speed={2} />, label: 'Running' },
  Completed: { color: 'green', icon: <Check size={12} strokeWidth={3} />, label: 'Success' },
  Failed: { color: 'red', icon: <X size={12} strokeWidth={3} />, label: 'Failed' },
  Cancelled: { color: 'gray', icon: <X size={12} />, label: 'Cancelled' },
};

function ToolCallItem({
  record,
  isLast,
}: {
  record: ToolCallRecord;
  isLast: boolean;
}) {
  const [expanded, setExpanded] = useState(false);
  const config = statusConfig[record.status] ?? statusConfig.Pending;

  const inputStr = record.input
    ? typeof record.input === 'string'
      ? record.input
      : JSON.stringify(record.input, null, 2)
    : null;

  const outputStr = record.output
    ? typeof record.output === 'string'
      ? record.output
      : JSON.stringify(record.output, null, 2)
    : null;

  return (
    <div style={{ display: 'flex', gap: 10, position: 'relative' }}>
      <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', flexShrink: 0 }}>
        <div
          style={{
            width: 22,
            height: 22,
            borderRadius: 4,
            background: `var(--mantine-color-${config.color}-1)`,
            border: `1px solid var(--mantine-color-${config.color}-3)`,
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            color: `var(--mantine-color-${config.color}-6)`,
            flexShrink: 0,
          }}
        >
          <Wrench size={11} />
        </div>
        {!isLast && (
          <div
            style={{
              width: 1,
              flex: 1,
              minHeight: 8,
              background: 'var(--exec-connector)',
            }}
          />
        )}
      </div>

      <div style={{ flex: 1, minWidth: 0, paddingBottom: isLast ? 0 : 4 }}>
        <UnstyledButton
          onClick={() => setExpanded(!expanded)}
          w="100%"
          style={{
            borderRadius: 6,
            padding: '4px 6px',
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
            <Text size="xs" fw={500} style={{ fontFamily: 'monospace' }}>
              {record.toolName}
            </Text>
            <Badge
              color={config.color}
              variant="light"
              size="xs"
              leftSection={config.icon}
              style={{ flexShrink: 0 }}
            >
              {config.label}
            </Badge>
            {record.duration !== null && (
              <Text size="xs" c="dimmed" style={{ flexShrink: 0, fontVariantNumeric: 'tabular-nums' }}>
                {record.duration < 1000 ? `${record.duration}ms` : `${(record.duration / 1000).toFixed(1)}s`}
              </Text>
            )}
            <Box style={{ color: 'var(--mantine-color-dimmed)', flexShrink: 0 }}>
              {expanded ? <ChevronDown size={12} /> : <ChevronRight size={12} />}
            </Box>
          </Group>
        </UnstyledButton>

        {!expanded && record.error && (
          <Box
            mt={2}
            mx={4}
            p="xs"
            style={{
              background: 'var(--exec-err-bg)',
              border: '1px solid var(--exec-err-border)',
              borderRadius: 4,
            }}
          >
            <Group gap={4} wrap="nowrap" align="flex-start">
              <AlertCircle size={11} color="var(--exec-err-color)" style={{ flexShrink: 0, marginTop: 1 }} />
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
                {record.error}
              </Text>
            </Group>
          </Box>
        )}

        <Collapse expanded={expanded}>
          <Stack gap={6} mt={4}>
            {inputStr && (
              <CodeViewer label="Input" code={inputStr} language="json" maxHeight={100} />
            )}
            {outputStr && (
              <CodeViewer label="Output" code={outputStr} language="json" maxHeight={120} />
            )}
            {!inputStr && !outputStr && (
              <Text size="xs" c="dimmed" ta="center" py="xs">
                No input/output data
              </Text>
            )}
          </Stack>
        </Collapse>
      </div>
    </div>
  );
}

export function ToolCallChain({ toolCalls }: ToolCallChainProps) {
  if (toolCalls.length === 0) {
    return (
      <Text size="xs" c="dimmed" ta="center" py="xs">
        No tool calls
      </Text>
    );
  }

  return (
    <div style={{ display: 'flex', flexDirection: 'column' }}>
      {toolCalls.map((call, index) => (
        <ToolCallItem
          key={call.id}
          record={call}
          isLast={index === toolCalls.length - 1}
        />
      ))}
    </div>
  );
}

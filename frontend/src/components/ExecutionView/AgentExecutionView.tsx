import { useState } from 'react';
import { Stack, Text, Box, Collapse, UnstyledButton, Group, Badge } from '@mantine/core';
import {
  Check,
  X,
  Clock,
  Loader,
  ChevronRight,
  ChevronDown,
  Bot,
  RefreshCw,
  Layers,
} from 'lucide-react';
import { ToolCallChain } from './ToolCallChain.tsx';
import { LLMThinkingView } from './LLMThinkingView.tsx';
import type {
  AgentExecutionData,
  AgentIteration,
  ExecutionStatus,
} from '../../types/agent-execution.ts';

interface AgentExecutionViewProps {
  data: AgentExecutionData;
  isStreaming?: boolean;
}

const statusConfig: Record<ExecutionStatus, { color: string; icon: React.ReactNode; label: string }> = {
  Pending: { color: 'gray', icon: <Clock size={14} />, label: 'Pending' },
  Running: { color: 'blue', icon: <Loader size={14} speed={2} />, label: 'Running' },
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

function IterationGroup({
  iteration,
  isLast,
  isExpanded,
  onToggle,
  systemPrompt,
}: {
  iteration: AgentIteration;
  isLast: boolean;
  isExpanded: boolean;
  onToggle: () => void;
  systemPrompt?: string | null;
}) {
  const duration = formatDuration(iteration.startedAt, iteration.completedAt);

  return (
    <div style={{ display: 'flex', gap: 10, position: 'relative' }}>
      <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', flexShrink: 0 }}>
        <div
          style={{
            width: 24,
            height: 24,
            borderRadius: '50%',
            background: 'var(--mantine-color-indigo-1)',
            border: '1.5px solid var(--mantine-color-indigo-3)',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            color: 'var(--mantine-color-indigo-6)',
            flexShrink: 0,
            fontSize: 10,
            fontWeight: 600,
          }}
        >
          {iteration.index + 1}
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

      <div style={{ flex: 1, minWidth: 0, paddingBottom: isLast ? 0 : 4 }}>
        <UnstyledButton
          onClick={onToggle}
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
            <Text size="xs" fw={500}>
              Iteration {iteration.index + 1}
            </Text>
            {iteration.toolCalls.length > 0 && (
              <Badge size="xs" variant="light" color="indigo">
                {iteration.toolCalls.length} tool{iteration.toolCalls.length > 1 ? 's' : ''}
              </Badge>
            )}
            {duration && (
              <Text size="xs" c="dimmed" style={{ flexShrink: 0, fontVariantNumeric: 'tabular-nums' }}>
                {duration}
              </Text>
            )}
            <Box style={{ color: 'var(--mantine-color-dimmed)', flexShrink: 0 }}>
              {isExpanded ? <ChevronDown size={14} /> : <ChevronRight size={14} />}
            </Box>
          </Group>
        </UnstyledButton>

        <Collapse expanded={isExpanded}>
          <Stack gap="sm" mt={4} ml={4}>
            <LLMThinkingView
              chunks={iteration.llmChunks}
              systemPrompt={systemPrompt}
              isStreaming={false}
            />
            {iteration.toolCalls.length > 0 && (
              <Box ml={2}>
                <ToolCallChain toolCalls={iteration.toolCalls} />
              </Box>
            )}
          </Stack>
        </Collapse>
      </div>
    </div>
  );
}

function SubRecordItem({
  subRecord,
  isExpanded,
  onToggle,
}: {
  subRecord: AgentExecutionData['subRecords'][0];
  isExpanded: boolean;
  onToggle: () => void;
}) {
  const config = statusConfig[subRecord.status] ?? statusConfig.Pending;

  return (
    <Box
      ml={28}
      pl="sm"
      style={{
        borderLeft: '2px solid var(--mantine-color-gray-3)',
      }}
    >
      <UnstyledButton
        onClick={onToggle}
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
          <Box
            style={{
              width: 18,
              height: 18,
              borderRadius: 4,
              background: `var(--mantine-color-${config.color}-1)`,
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
              flexShrink: 0,
            }}
          >
            <Bot size={10} color={`var(--mantine-color-${config.color}-6)`} />
          </Box>
          <Text size="xs" fw={500} flex={1} ta="left">
            {subRecord.agentName}
          </Text>
          <Badge color={config.color} variant="light" size="xs">
            {config.label}
          </Badge>
          <Box style={{ color: 'var(--mantine-color-dimmed)', flexShrink: 0 }}>
            {isExpanded ? <ChevronDown size={12} /> : <ChevronRight size={12} />}
          </Box>
        </Group>
      </UnstyledButton>

      <Collapse expanded={isExpanded}>
        <Box mt={4}>
          {subRecord.records.map((iteration, index) => (
            <IterationGroup
              key={iteration.index}
              iteration={iteration}
              isLast={index === subRecord.records.length - 1}
              isExpanded={false}
              onToggle={() => {}}
            />
          ))}
        </Box>
      </Collapse>
    </Box>
  );
}

export function AgentExecutionView({ data, isStreaming }: AgentExecutionViewProps) {
  const [expandedIterations, setExpandedIterations] = useState<Record<number, boolean>>({});
  const [expandedSubRecords, setExpandedSubRecords] = useState<Record<string, boolean>>({});

  const { agentInfo, iterations, subRecords, systemPrompt } = data;
  const statusInfo = statusConfig[agentInfo.status] ?? statusConfig.Pending;
  const duration = formatDuration(agentInfo.startedAt, agentInfo.completedAt);

  const toggleIteration = (index: number) => {
    setExpandedIterations((prev) => ({ ...prev, [index]: !prev[index] }));
  };

  const toggleSubRecord = (parentId: string) => {
    setExpandedSubRecords((prev) => ({ ...prev, [parentId]: !prev[parentId] }));
  };

  return (
    <Stack gap="sm">
      <Group gap="xs" wrap="nowrap">
        <Box
          style={{
            width: 28,
            height: 28,
            borderRadius: 6,
            background: 'var(--mantine-color-indigo-1)',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            flexShrink: 0,
          }}
        >
          <Bot size={16} color="var(--mantine-color-indigo-6)" />
        </Box>
        <Stack gap={0} flex={1}>
          <Text size="sm" fw={600}>
            Agent Execution
          </Text>
          <Group gap="xs" wrap="nowrap">
            <Text size="xs" c="dimmed" style={{ fontFamily: 'monospace' }}>
              {agentInfo.model}
            </Text>
            <Text size="xs" c="dimmed">·</Text>
            <Group gap={3} wrap="nowrap">
              <RefreshCw size={10} color="var(--mantine-color-dimmed)" />
              <Text size="xs" c="dimmed">
                {agentInfo.iterationCount} iteration{agentInfo.iterationCount !== 1 ? 's' : ''}
              </Text>
            </Group>
          </Group>
        </Stack>
        <Badge
          color={statusInfo.color}
          variant="light"
          size="sm"
          leftSection={statusInfo.icon}
        >
          {statusInfo.label}
        </Badge>
        {duration && (
          <Text size="xs" c="dimmed" style={{ flexShrink: 0 }}>
            {duration}
          </Text>
        )}
      </Group>

      {agentInfo.errorMessage && (
        <Box
          p="xs"
          style={{
            background: 'var(--exec-err-bg)',
            border: '1px solid var(--exec-err-border)',
            borderRadius: 6,
          }}
        >
          <Text size="xs" style={{ color: 'var(--exec-err-color)', lineHeight: 1.5 }}>
            {agentInfo.errorMessage}
          </Text>
        </Box>
      )}

      <Box
        style={{
          borderRadius: 6,
          border: '1px solid var(--exec-connector)',
          background: 'var(--mantine-color-gray-0)',
          padding: 'sm',
        }}
      >
        <Stack gap="xs">
          {iterations.map((iteration, index) => (
            <IterationGroup
              key={iteration.index}
              iteration={iteration}
              isLast={index === iterations.length - 1 && subRecords.length === 0}
              isExpanded={!!expandedIterations[iteration.index]}
              onToggle={() => toggleIteration(iteration.index)}
              systemPrompt={systemPrompt}
            />
          ))}

          {subRecords.length > 0 && (
            <Box mt="xs">
              <Group gap="xs" mb={4}>
                <Layers size={12} color="var(--mantine-color-dimmed)" />
                <Text size="xs" fw={500} c="dimmed">
                  Sub-agents ({subRecords.length})
                </Text>
              </Group>
              <Stack gap="xs">
                {subRecords.map((subRecord) => (
                  <SubRecordItem
                    key={subRecord.parentId}
                    subRecord={subRecord}
                    isExpanded={!!expandedSubRecords[subRecord.parentId]}
                    onToggle={() => toggleSubRecord(subRecord.parentId)}
                  />
                ))}
              </Stack>
            </Box>
          )}
        </Stack>
      </Box>

      {isStreaming && agentInfo.status === 'Running' && (
        <Group gap="xs" justify="center">
          <Loader size={12} speed={2} />
          <Text size="xs" c="dimmed">
            Streaming...
          </Text>
        </Group>
      )}
    </Stack>
  );
}

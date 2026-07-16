import { Stack, Text, Badge, Group, Code, Paper, ScrollArea } from '@mantine/core';
import { Diff, Plus, Minus, ArrowRight, Unlink, ArrowLeftRight } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import type { StructuredDiff } from '../../types/workflow.ts';

interface IDiffPanelProps {
  diff: StructuredDiff[];
  highlightedNodeIds: string[];
  onNodeHighlight: (nodeIds: string[]) => void;
}

function DiffIcon({ op }: { op: string }) {
  switch (op) {
    case 'modify': return <ArrowLeftRight size={14} />;
    case 'add': return <Plus size={14} />;
    case 'remove': return <Minus size={14} />;
    case 'connect': return <ArrowRight size={14} />;
    case 'disconnect': return <Unlink size={14} />;
    default: return <Diff size={14} />;
  }
}

function DiffColor({ op }: { op: string }) {
  switch (op) {
    case 'modify': return 'yellow';
    case 'add': return 'green';
    case 'remove': return 'red';
    case 'connect': return 'blue';
    case 'disconnect': return 'orange';
    default: return 'gray';
  }
}

function DiffDescription({ entry }: { entry: StructuredDiff }) {
  const { t } = useTranslation('parameterPanel');
  switch (entry.op) {
    case 'modify':
      return (
        <Stack gap={4}>
          <Text size="xs">{t('diffPanel.field')}: <Code>{entry.field}</Code></Text>
          <Group gap={4} wrap="nowrap" align="flex-start">
            <Text size="xs" c="red" style={{ textDecoration: 'line-through' }}>{JSON.stringify(entry.before)}</Text>
            <ArrowRight size={12} style={{ flexShrink: 0 }} />
            <Text size="xs" c="green">{JSON.stringify(entry.after)}</Text>
          </Group>
        </Stack>
      );
    case 'add':
      return <Text size="xs">{t('diffPanel.addedNode')} <Code>{entry.nodeId}</Code></Text>;
    case 'remove':
      return <Text size="xs">{t('diffPanel.removedNode')} <Code>{entry.nodeId}</Code></Text>;
    case 'connect':
      return <Text size="xs">{t('diffPanel.newConnection')}: {String(entry.after ?? '')}</Text>;
    case 'disconnect':
      return <Text size="xs">{t('diffPanel.removedConnection')}: {String(entry.before ?? '')}</Text>;
    default:
      return <Text size="xs">{entry.op}: {entry.nodeId ?? ''} {entry.field ?? ''}</Text>;
  }
}

export function DiffPanel({ diff, highlightedNodeIds, onNodeHighlight }: IDiffPanelProps) {
  const { t } = useTranslation('parameterPanel');
  const handleMouseEnter = (nodeId?: string) => {
    if (nodeId) onNodeHighlight([nodeId]);
  };
  const handleMouseLeave = () => onNodeHighlight([]);

  return (
    <Paper p="sm" withBorder>
      <Stack gap="xs">
        <Group gap={4}>
          <Diff size={14} />
          <Text fw={600} size="xs" tt="uppercase">{t('diffPanel.changes')} ({diff.length})</Text>
        </Group>
        <ScrollArea h={300}>
          <Stack gap={4}>
            {diff.map((entry, idx) => (
              <Paper
                key={`${entry.op}-${entry.nodeId ?? entry.field}-${idx}`}
                p={4}
                withBorder
                style={{ cursor: entry.nodeId ? 'pointer' : 'default' }}
                onMouseEnter={() => handleMouseEnter(entry.nodeId)}
                onMouseLeave={handleMouseLeave}
                bg={highlightedNodeIds.includes(entry.nodeId ?? '') ? 'var(--mantine-color-yellow-light)' : undefined}
              >
                <Group gap={4} wrap="nowrap" align="flex-start">
                  <Badge size="xs" color={DiffColor({ op: entry.op })} leftSection={<DiffIcon op={entry.op} />}>
                    {entry.op}
                  </Badge>
                  <DiffDescription entry={entry} />
                </Group>
              </Paper>
            ))}
          </Stack>
        </ScrollArea>
      </Stack>
    </Paper>
  );
}

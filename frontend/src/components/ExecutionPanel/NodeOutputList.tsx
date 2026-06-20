import { useState } from 'react';
import { Stack, Paper, Group, Text, Badge, UnstyledButton, ScrollArea, Box } from '@mantine/core';
import { ChevronRight, ChevronDown } from 'lucide-react';
import type { NodeExecutionRecordDto, ExecutionStatus } from '../../types/workflow.ts';

interface NodeOutputListProps {
  records: NodeExecutionRecordDto[];
}

const statusColors: Record<ExecutionStatus, string> = {
  Pending: 'orange',
  Running: 'blue',
  Completed: 'green',
  Failed: 'red',
  Cancelled: 'gray',
};

export function NodeOutputList({ records }: NodeOutputListProps) {
  const [expanded, setExpanded] = useState<Record<string, boolean>>({});

  const toggle = (id: string) => {
    setExpanded((prev) => ({ ...prev, [id]: !prev[id] }));
  };

  if (records.length === 0) {
    return <Text size="sm" c="dimmed" ta="center" py="md">No node records</Text>;
  }

  return (
    <Stack gap="xs">
      {records.map((record) => (
        <Paper key={record.id} withBorder radius="sm" style={{ overflow: 'hidden' }}>
          <UnstyledButton
            w="100%"
            px="sm"
            py={6}
            onClick={() => toggle(record.id)}
          >
            <Group gap="xs" wrap="nowrap">
              {expanded[record.id]
                ? <ChevronDown size={12} color="var(--mantine-color-dimmed)" />
                : <ChevronRight size={12} color="var(--mantine-color-dimmed)" />
              }
              <Text size="sm" fw={500} flex={1} truncate>
                {record.nodeDefinitionId}
              </Text>
              <Badge size="xs" variant="light" color={statusColors[record.status]}>
                {record.status}
              </Badge>
            </Group>
          </UnstyledButton>
          {expanded[record.id] && (
            <Stack gap="xs" p="sm" pt={0}>
              {record.output !== undefined && record.output !== null && (
                <Box>
                  <Text size="xs" fw={600} c="dimmed" mb={4}>Output:</Text>
                  <ScrollArea.Autosize mah={200} type="auto" offsetScrollbars>
                    <pre className="output-json">
                      {typeof record.output === 'string'
                        ? record.output
                        : JSON.stringify(record.output, null, 2)}
                    </pre>
                  </ScrollArea.Autosize>
                </Box>
              )}
              {record.inputs && (
                <Box>
                  <Text size="xs" fw={600} c="dimmed" mb={4}>Inputs:</Text>
                  <ScrollArea.Autosize mah={200} type="auto" offsetScrollbars>
                    <pre className="output-json">
                      {JSON.stringify(record.inputs, null, 2)}
                    </pre>
                  </ScrollArea.Autosize>
                </Box>
              )}
              {record.resolvedParameters && (
                <Box>
                  <Text size="xs" fw={600} c="dimmed" mb={4}>Resolved Parameters:</Text>
                  <ScrollArea.Autosize mah={200} type="auto" offsetScrollbars>
                    <pre className="output-json">
                      {JSON.stringify(record.resolvedParameters, null, 2)}
                    </pre>
                  </ScrollArea.Autosize>
                </Box>
              )}
            </Stack>
          )}
        </Paper>
      ))}
    </Stack>
  );
}

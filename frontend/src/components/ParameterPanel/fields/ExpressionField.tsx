import { useState } from 'react';
import { TextInput, Popover, Text, Stack, Code, Group } from '@mantine/core';
import { Braces } from 'lucide-react';
import type { ParameterDefinition } from '../../../types/workflow.ts';
import { useWorkflowStore } from '../../../stores/workflowStore.ts';
import { getExpressionHints } from '../../../utils/expressionHint.ts';

interface ExpressionFieldProps {
  definition: ParameterDefinition;
  value: unknown;
  onChange: (value: string) => void;
  error?: string;
}

/**
 * 表达式输入，聚焦时弹出可用变量提示。
 */
export function ExpressionField({ definition, value, onChange, error }: ExpressionFieldProps) {
  const [opened, setOpened] = useState(false);
  const nodes = useWorkflowStore((s) => s.nodes);
  const hintGroups = getExpressionHints(nodes);

  return (
    <Popover opened={opened} position="bottom-start" width={320} shadow="sm">
      <Popover.Target>
        <TextInput
          label={definition.displayName}
          description={definition.description ?? undefined}
          error={error}
          required={definition.required}
          value={String(value ?? '')}
          onChange={(e) => onChange(e.target.value)}
          onFocus={() => setOpened(true)}
          onBlur={() => setOpened(false)}
          placeholder="Type expression or use {{ variable }}"
          rightSection={<Braces size={16} style={{ color: 'var(--mantine-color-dimmed)' }} />}
        />
      </Popover.Target>
      <Popover.Dropdown>
        <Stack gap="xs">
          <Text size="xs" fw={600} c="dimmed">Available variables</Text>
          {hintGroups.map((group) => (
            <div key={group.label}>
              <Text size="xs" fw={500} c="dimmed" mb={2}>{group.label}</Text>
              <Group gap={4}>
                {group.variables.map((v) => (
                  <Code key={v}>{v}</Code>
                ))}
              </Group>
            </div>
          ))}
        </Stack>
      </Popover.Dropdown>
    </Popover>
  );
}

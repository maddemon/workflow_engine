import { Switch, Group, Text } from '@mantine/core';
import { InfoTooltip } from './InfoTooltip.tsx';
import type { ParameterDefinition } from '../../../types/workflow.ts';

interface BooleanFieldProps {
  definition: ParameterDefinition;
  value: unknown;
  onChange: (value: boolean) => void;
  error?: string;
}

export function BooleanField({ definition, value, onChange, error }: BooleanFieldProps) {
  return (
    <Group
      justify="space-between"
      align="center"
      onClick={() => onChange(!value)}
      style={{ cursor: 'pointer' }}
      p={4}
    >
      <Switch
        checked={!!value}
        onChange={(e) => onChange(e.currentTarget.checked)}
        error={error}
        size="sm"
        onClick={(e) => e.stopPropagation()}
      />
      <Group gap={4} style={{ flex: 1 }}>
        <Text size="xs" fw={400}>{definition.displayName}</Text>
        {definition.description && <InfoTooltip label={definition.description} />}
      </Group>
    </Group>
  );
}

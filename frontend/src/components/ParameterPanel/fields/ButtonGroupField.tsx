import { Button, Group, Text } from '@mantine/core';
import { InfoTooltip } from './InfoTooltip.tsx';
import type { ParameterDefinition } from '../../../types/workflow.ts';

interface ButtonGroupFieldProps {
  definition: ParameterDefinition;
  value: unknown;
  onChange: (value: string) => void;
  error?: string;
}

export function ButtonGroupField({ definition, value, onChange, error }: ButtonGroupFieldProps) {
  const options = definition.options ?? [];
  const current = String(value ?? '');

  return (
    <div>
      <Group gap={4} mb={4}>
        <Text size="xs" fw={400}>
          {definition.displayName}
          {definition.required && <span style={{ color: 'var(--mantine-color-error)' }}> *</span>}
        </Text>
        {definition.description && <InfoTooltip label={definition.description} />}
      </Group>
      <Button.Group>
        {options.map((opt) => (
          <Button
            key={opt.value}
            size="xs"
            variant={current === opt.value ? 'filled' : 'default'}
            onClick={() => onChange(opt.value)}
            style={{ flex: 1 }}
          >
            {opt.label}
          </Button>
        ))}
      </Button.Group>
      {error && (
        <Text size="xs" c="red" mt={4}>{error}</Text>
      )}
    </div>
  );
}

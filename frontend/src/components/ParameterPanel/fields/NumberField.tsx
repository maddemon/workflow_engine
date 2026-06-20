import { NumberInput, Group, Text } from '@mantine/core';
import { InfoTooltip } from './InfoTooltip.tsx';
import type { ParameterDefinition } from '../../../types/workflow.ts';

interface NumberFieldProps {
  definition: ParameterDefinition;
  value: unknown;
  onChange: (value: number | '') => void;
  error?: string;
}

export function NumberField({ definition, value, onChange, error }: NumberFieldProps) {
  const numValue = typeof value === 'number' ? value : typeof value === 'string' && value !== '' ? Number(value) : '';
  return (
    <div>
      <Group gap={4} mb={4}>
        <Text size="xs" fw={400}>
          {definition.displayName}
          {definition.required && <span style={{ color: 'var(--mantine-color-error)' }}> *</span>}
        </Text>
        {definition.description && <InfoTooltip label={definition.description} />}
      </Group>
      <NumberInput
        error={error}
        value={numValue}
        onChange={(v) => onChange(typeof v === 'number' ? v : '')}
        placeholder={`Enter ${definition.displayName.toLowerCase()}`}
      />
    </div>
  );
}

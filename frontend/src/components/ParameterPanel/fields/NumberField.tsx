import { NumberInput } from '@mantine/core';
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
    <NumberInput
      label={definition.displayName}
      description={definition.description ?? undefined}
      error={error}
      required={definition.required}
      value={numValue}
      onChange={(v) => onChange(typeof v === 'number' ? v : '')}
      placeholder={`Enter ${definition.displayName.toLowerCase()}`}
    />
  );
}

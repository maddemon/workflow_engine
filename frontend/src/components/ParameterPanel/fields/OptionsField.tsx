import { Select } from '@mantine/core';
import type { ParameterDefinition, Option } from '../../../types/workflow.ts';

interface OptionsFieldProps {
  definition: ParameterDefinition;
  value: unknown;
  onChange: (value: string) => void;
  error?: string;
}

export function OptionsField({ definition, value, onChange, error }: OptionsFieldProps) {
  const options: Option[] = definition.options ?? [];
  return (
    <Select
      label={definition.displayName}
      description={definition.description ?? undefined}
      error={error}
      required={definition.required}
      value={String(value ?? '')}
      onChange={(v) => onChange(v ?? '')}
      placeholder="-- Select --"
      data={options.map((opt) => ({ label: opt.label, value: opt.value }))}
    />
  );
}

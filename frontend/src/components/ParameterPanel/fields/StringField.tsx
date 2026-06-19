import { TextInput } from '@mantine/core';
import type { ParameterDefinition } from '../../../types/workflow.ts';

interface StringFieldProps {
  definition: ParameterDefinition;
  value: unknown;
  onChange: (value: string) => void;
  error?: string;
}

export function StringField({ definition, value, onChange, error }: StringFieldProps) {
  return (
    <TextInput
      label={definition.displayName}
      description={definition.description ?? undefined}
      error={error}
      required={definition.required}
      value={String(value ?? '')}
      onChange={(e) => onChange(e.target.value)}
      placeholder={`Enter ${definition.displayName.toLowerCase()}`}
    />
  );
}

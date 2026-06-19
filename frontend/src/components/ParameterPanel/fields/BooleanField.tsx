import { Switch } from '@mantine/core';
import type { ParameterDefinition } from '../../../types/workflow.ts';

interface BooleanFieldProps {
  definition: ParameterDefinition;
  value: unknown;
  onChange: (value: boolean) => void;
  error?: string;
}

export function BooleanField({ definition, value, onChange, error }: BooleanFieldProps) {
  return (
    <Switch
      label={definition.displayName}
      description={definition.description ?? undefined}
      error={error}
      checked={!!value}
      onChange={(e) => onChange(e.currentTarget.checked)}
    />
  );
}

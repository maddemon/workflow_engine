import { Textarea } from '@mantine/core';
import type { ParameterDefinition } from '../../../types/workflow.ts';

interface CodeFieldProps {
  definition: ParameterDefinition;
  value: unknown;
  onChange: (value: string) => void;
  error?: string;
}

export function CodeField({ definition, value, onChange, error }: CodeFieldProps) {
  return (
    <Textarea
      label={definition.displayName}
      description={definition.description ?? undefined}
      error={error}
      required={definition.required}
      value={String(value ?? '')}
      onChange={(e) => onChange(e.target.value)}
      autosize
      minRows={6}
      maxRows={20}
      spellCheck={false}
      styles={{ input: { fontFamily: 'var(--mantine-font-family-monospace)', fontSize: 12, minHeight: 160 } }}
    />
  );
}

import { Textarea } from '@mantine/core';
import type { ParameterDefinition } from '../../../types/workflow.ts';

interface TextAreaFieldProps {
  definition: ParameterDefinition;
  value: unknown;
  onChange: (value: string) => void;
  error?: string;
}

/**
 * 多行文本，支持自动高度。
 */
export function TextAreaField({ definition, value, onChange, error }: TextAreaFieldProps) {
  return (
    <Textarea
      label={definition.displayName}
      description={definition.description ?? undefined}
      error={error}
      required={definition.required}
      value={String(value ?? '')}
      onChange={(e) => onChange(e.target.value)}
      autosize
      minRows={3}
      maxRows={10}
      placeholder={`Enter ${definition.displayName.toLowerCase()}`}
    />
  );
}

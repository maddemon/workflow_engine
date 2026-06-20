import { Textarea, Group, Text } from '@mantine/core';
import { InfoTooltip } from './InfoTooltip.tsx';
import type { ParameterDefinition } from '../../../types/workflow.ts';

interface TextAreaFieldProps {
  definition: ParameterDefinition;
  value: unknown;
  onChange: (value: string) => void;
  error?: string;
}

export function TextAreaField({ definition, value, onChange, error }: TextAreaFieldProps) {
  return (
    <div>
      <Group gap={4} mb={4}>
        <Text size="xs" fw={400}>
          {definition.displayName}
          {definition.required && <span style={{ color: 'var(--mantine-color-error)' }}> *</span>}
        </Text>
        {definition.description && <InfoTooltip label={definition.description} />}
      </Group>
      <Textarea
        error={error}
        value={String(value ?? '')}
        onChange={(e) => onChange(e.target.value)}
        autosize
        minRows={3}
        maxRows={10}
        placeholder={`Enter ${definition.displayName.toLowerCase()}`}
      />
    </div>
  );
}

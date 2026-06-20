import { Textarea, Group, Text } from '@mantine/core';
import { InfoTooltip } from './InfoTooltip.tsx';
import type { ParameterDefinition } from '../../../types/workflow.ts';

interface CodeFieldProps {
  definition: ParameterDefinition;
  value: unknown;
  onChange: (value: string) => void;
  error?: string;
}

export function CodeField({ definition, value, onChange, error }: CodeFieldProps) {
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
        minRows={6}
        maxRows={20}
        spellCheck={false}
        styles={{ input: { fontFamily: 'var(--mantine-font-family-monospace)', fontSize: 12, minHeight: 160 } }}
      />
    </div>
  );
}

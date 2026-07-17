import { Textarea, Group, Text } from '@mantine/core';
import { InfoTooltip } from './InfoTooltip.tsx';
import { useParameterName } from '../useParameterName.ts';
import type { ParameterDefinition } from '../../../types/workflow.ts';
import { extractScriptSource } from '../../../utils/scriptValue.ts';

interface CodeFieldProps {
  definition: ParameterDefinition;
  value: unknown;
  onChange: (value: string) => void;
  error?: string;
}

export function CodeField({ definition, value, onChange, error }: CodeFieldProps) {
  const paramName = useParameterName();
  const label = paramName(definition.name, definition.displayName);
  return (
    <div>
      <Group gap={4} mb={4}>
        <Text size="xs" fw={400}>
          {label}
          {definition.required && <span style={{ color: 'var(--mantine-color-error)' }}> *</span>}
        </Text>
        {definition.description && <InfoTooltip label={definition.description} />}
      </Group>
      <Textarea
        error={error}
        value={extractScriptSource(value)}
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

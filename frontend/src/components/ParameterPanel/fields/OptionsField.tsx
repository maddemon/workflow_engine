import { Select, Group, Text } from '@mantine/core';
import { InfoTooltip } from './InfoTooltip.tsx';
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
    <div>
      <Group gap={4} mb={4}>
        <Text size="xs" fw={400}>
          {definition.displayName}
          {definition.required && <span style={{ color: 'var(--mantine-color-error)' }}> *</span>}
        </Text>
        {definition.description && <InfoTooltip label={definition.description} />}
      </Group>
      <Select
        error={error}
        value={String(value ?? '')}
        onChange={(v) => onChange(v ?? '')}
        placeholder="-- Select --"
        data={options.map((opt) => ({ label: opt.label, value: opt.value }))}
      />
    </div>
  );
}

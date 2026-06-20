import { Select, Group, Text } from '@mantine/core';
import { InfoTooltip } from './InfoTooltip.tsx';
import type { ParameterDefinition } from '../../../types/workflow.ts';

interface ResourceFieldProps {
  definition: ParameterDefinition;
  value: unknown;
  onChange: (value: string) => void;
  error?: string;
}

export function ResourceField({ definition, value, onChange, error }: ResourceFieldProps) {
  const options = definition.options ?? [];
  return (
    <div>
      <Group gap={4} mb={4}>
        <Text size="xs" fw={400}>
          {definition.displayName}
          {definition.required && <span style={{ color: 'var(--mantine-color-error)' }}> *</span>}
        </Text>
        <InfoTooltip label={definition.description ?? `Select a ${definition.resourceType ?? 'resource'}.`} />
      </Group>
      <Select
        error={error}
        value={String(value ?? '')}
        onChange={(v) => onChange(v ?? '')}
        placeholder={`-- Select ${definition.resourceType ?? 'resource'} --`}
        data={options.map((opt) => ({ label: opt.label, value: opt.value }))}
        searchable
      />
    </div>
  );
}

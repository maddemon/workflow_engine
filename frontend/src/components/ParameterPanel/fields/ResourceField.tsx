import { Select, Group, Text } from '@mantine/core';
import { useTranslation } from 'react-i18next';
import { InfoTooltip } from './InfoTooltip.tsx';
import { useParameterName } from '../useParameterName.ts';
import type { ParameterDefinition } from '../../../types/workflow.ts';

interface ResourceFieldProps {
  definition: ParameterDefinition;
  value: unknown;
  onChange: (value: string) => void;
  error?: string;
}

export function ResourceField({ definition, value, onChange, error }: ResourceFieldProps) {
  const { t } = useTranslation('parameterPanel');
  const paramName = useParameterName();
  const label = paramName(definition.name, definition.displayName);
  const options = definition.options ?? [];
  return (
    <div>
      <Group gap={4} mb={4}>
        <Text size="xs" fw={400}>
          {label}
          {definition.required && definition.defaultValue == null && <span style={{ color: 'var(--mantine-color-error)' }}> *</span>}
        </Text>
        <InfoTooltip label={definition.description ?? t('fields.resource.tooltip', { type: definition.resourceType ?? 'resource' })} />
      </Group>
      <Select
        error={error}
        value={String(value ?? '')}
        onChange={(v) => onChange(v ?? '')}
        placeholder={t('fields.resource.placeholder', { type: definition.resourceType ?? 'resource' })}
        data={options.map((opt) => ({ label: opt.label, value: opt.value }))}
        searchable
      />
    </div>
  );
}

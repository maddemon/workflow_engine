import { Select, Group, Text } from '@mantine/core';
import { useTranslation } from 'react-i18next';
import { InfoTooltip } from './InfoTooltip.tsx';
import { useParameterName } from '../useParameterName.ts';
import type { ParameterDefinition, Option } from '../../../types/workflow.ts';

interface OptionsFieldProps {
  definition: ParameterDefinition;
  value: unknown;
  onChange: (value: string) => void;
  error?: string;
}

export function OptionsField({ definition, value, onChange, error }: OptionsFieldProps) {
  const { t } = useTranslation('parameterPanel');
  const paramName = useParameterName();
  const label = paramName(definition.name, definition.displayName);
  const options: Option[] = definition.options ?? [];
  return (
    <div>
      <Group gap={4} mb={4}>
        <Text size="xs" fw={400}>
          {label}
          {definition.required && definition.defaultValue == null && <span style={{ color: 'var(--mantine-color-error)' }}> *</span>}
        </Text>
        {definition.description && <InfoTooltip label={definition.description} />}
      </Group>
      <Select
        error={error}
        value={String(value ?? '')}
        onChange={(v) => onChange(v ?? '')}
        placeholder={t('fields.select')}
        data={options.map((opt) => ({ label: opt.label, value: opt.value }))}
      />
    </div>
  );
}

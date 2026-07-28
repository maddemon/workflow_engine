import { TextInput, Group, Text } from '@mantine/core';
import { useTranslation } from 'react-i18next';
import { InfoTooltip } from './InfoTooltip.tsx';
import { useParameterName } from '../useParameterName.ts';
import type { ParameterDefinition } from '../../../types/workflow.ts';

interface StringFieldProps {
  definition: ParameterDefinition;
  value: unknown;
  onChange: (value: string) => void;
  error?: string;
}

export function StringField({ definition, value, onChange, error }: StringFieldProps) {
  const { t } = useTranslation('parameterPanel');
  const paramName = useParameterName();
  const label = paramName(definition.name, definition.displayName);
  return (
    <div>
      <Group gap={4} mb={4}>
        <Text size="xs" fw={400}>
          {label}
          {definition.required && definition.defaultValue == null && <span style={{ color: 'var(--mantine-color-error)' }}> *</span>}
        </Text>
        {definition.description && <InfoTooltip label={definition.description} />}
      </Group>
      <TextInput
        error={error}
        value={String(value ?? '')}
        onChange={(e) => onChange(e.target.value)}
        placeholder={t('fields.placeholder', { name: label.toLowerCase() })}
      />
    </div>
  );
}

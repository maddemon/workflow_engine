import { PasswordInput, Group, Text } from '@mantine/core';
import { InfoTooltip } from './InfoTooltip.tsx';
import type { ParameterDefinition } from '../../../types/workflow.ts';

interface SecretFieldProps {
  definition: ParameterDefinition;
  value: unknown;
  onChange: (value: string) => void;
  error?: string;
}

export function SecretField({ definition, value, onChange, error }: SecretFieldProps) {
  return (
    <div>
      <Group gap={4} mb={4}>
        <Text size="xs" fw={400}>
          {definition.displayName}
          {definition.required && <span style={{ color: 'var(--mantine-color-error)' }}> *</span>}
        </Text>
        {definition.description && <InfoTooltip label={definition.description} />}
      </Group>
      <PasswordInput
        error={error}
        value={String(value ?? '')}
        onChange={(e) => onChange(e.target.value)}
        placeholder={`Enter ${definition.displayName.toLowerCase()}`}
      />
    </div>
  );
}

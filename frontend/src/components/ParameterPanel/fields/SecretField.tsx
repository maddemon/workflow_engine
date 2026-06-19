import { PasswordInput } from '@mantine/core';
import type { ParameterDefinition } from '../../../types/workflow.ts';

interface SecretFieldProps {
  definition: ParameterDefinition;
  value: unknown;
  onChange: (value: string) => void;
  error?: string;
}

/**
 * 密码型输入，用于 password/token/secret 等敏感字段。
 */
export function SecretField({ definition, value, onChange, error }: SecretFieldProps) {
  return (
    <PasswordInput
      label={definition.displayName}
      description={definition.description ?? undefined}
      error={error}
      required={definition.required}
      value={String(value ?? '')}
      onChange={(e) => onChange(e.target.value)}
      placeholder={`Enter ${definition.displayName.toLowerCase()}`}
    />
  );
}

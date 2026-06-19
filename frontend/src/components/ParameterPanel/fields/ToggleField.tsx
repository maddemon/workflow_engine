import { Switch } from '@mantine/core';
import type { ParameterDefinition } from '../../../types/workflow.ts';

interface ToggleFieldProps {
  definition: ParameterDefinition;
  value: unknown;
  onChange: (value: boolean) => void;
  error?: string;
}

/**
 * 开关样式布尔值，Boolean 类型默认渲染。
 */
export function ToggleField({ definition, value, onChange, error }: ToggleFieldProps) {
  return (
    <Switch
      label={definition.displayName}
      description={definition.description ?? undefined}
      error={error}
      checked={!!value}
      onChange={(e) => onChange(e.currentTarget.checked)}
    />
  );
}

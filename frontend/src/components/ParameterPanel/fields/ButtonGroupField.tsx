import { SegmentedControl, Text } from '@mantine/core';
import type { ParameterDefinition } from '../../../types/workflow.ts';

interface ButtonGroupFieldProps {
  definition: ParameterDefinition;
  value: unknown;
  onChange: (value: string) => void;
  error?: string;
}

/**
 * 横向按钮组，用于少量互斥选项（如 HTTP method）。
 * 选项 > 6 时不建议使用，会自动回退到 OptionsField（由 FieldResolver 控制）。
 */
export function ButtonGroupField({ definition, value, onChange, error }: ButtonGroupFieldProps) {
  const options = definition.options ?? [];
  return (
    <div>
      <Text size="sm" fw={500} mb={4}>
        {definition.displayName}
        {definition.required && <span style={{ color: 'var(--mantine-color-error)' }}> *</span>}
      </Text>
      <SegmentedControl
        value={String(value ?? '')}
        onChange={(v) => onChange(v)}
        data={options.map((opt) => ({ label: opt.label, value: opt.value }))}
        fullWidth
      />
      {definition.description && (
        <Text size="xs" c="dimmed" mt={4}>{definition.description}</Text>
      )}
      {error && (
        <Text size="xs" c="red" mt={4}>{error}</Text>
      )}
    </div>
  );
}

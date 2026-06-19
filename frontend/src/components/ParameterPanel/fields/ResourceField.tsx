import { Select } from '@mantine/core';
import type { ParameterDefinition } from '../../../types/workflow.ts';

interface ResourceFieldProps {
  definition: ParameterDefinition;
  value: unknown;
  onChange: (value: string) => void;
  error?: string;
}

/**
 * 动态资源选择，根据 resourceType 调用不同 API 拉取选项。
 * 本次先做静态选项框架，动态拉取后续按需扩展。
 */
export function ResourceField({ definition, value, onChange, error }: ResourceFieldProps) {
  // TODO: 根据 definition.resourceType 调用对应 API 拉取动态选项（如企业微信部门/应用/标签）
  // 当前使用 definition.options 作为静态选项框架
  const options = definition.options ?? [];
  return (
    <Select
      label={definition.displayName}
      description={definition.description ?? `Select a ${definition.resourceType ?? 'resource'}.`}
      error={error}
      required={definition.required}
      value={String(value ?? '')}
      onChange={(v) => onChange(v ?? '')}
      placeholder={`-- Select ${definition.resourceType ?? 'resource'} --`}
      data={options.map((opt) => ({ label: opt.label, value: opt.value }))}
      searchable
    />
  );
}

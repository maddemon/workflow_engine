import type { ComponentType } from 'react';
import type { ParameterDefinition } from '../../types/workflow.ts';
import { resolveFieldComponent } from './FieldComponentMap.ts';
import { resolveHint } from './resolveHint.ts';

interface FieldResolverProps {
  definition: ParameterDefinition;
  value: unknown;
  onChange: (value: unknown) => void;
  error?: string;
  projectId?: string | null;
}

/**
 * 渲染已解析出的字段组件。Component 以 prop 形式传入，
 * 避免在渲染期间创建组件（react-hooks/static-components）。
 */
function ResolvedField({ Component, ...props }: { Component: ComponentType<FieldResolverProps> } & FieldResolverProps) {
  return <Component {...props} />;
}

/**
 * 字段分发组件。
 * 优先级：definition.hint > 前端自动规则 > definition.type。
 */
export function FieldResolver({ definition, value, onChange, error, projectId }: FieldResolverProps) {
  const hint = resolveHint(definition);
  const FieldComponent = resolveFieldComponent(hint, definition.type);
  return (
    <ResolvedField
      Component={FieldComponent}
      definition={definition}
      value={value}
      onChange={onChange}
      error={error}
      projectId={projectId}
    />
  );
}

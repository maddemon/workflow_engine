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
 * 字段分发组件。
 * 优先级：definition.hint > 前端自动规则 > definition.type。
 */
export function FieldResolver({ definition, value, onChange, error, projectId }: FieldResolverProps) {
  const hint = resolveHint(definition);

  const Field = resolveFieldComponent(hint, definition.type);
  return <Field definition={definition} value={value} onChange={onChange} error={error} projectId={projectId} />;
}

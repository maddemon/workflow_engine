import type { ParameterDefinition } from '../../types/workflow.ts';
import { StringField } from './fields/StringField.tsx';
import { hintFieldMap, typeFieldMap } from './FieldComponentMap.ts';
import { resolveHint } from './resolveHint.ts';

interface FieldResolverProps {
  definition: ParameterDefinition;
  value: unknown;
  onChange: (value: unknown) => void;
  error?: string;
}

/**
 * 字段分发组件。
 * 优先级：definition.hint > 前端自动规则 > definition.type。
 */
export function FieldResolver({ definition, value, onChange, error }: FieldResolverProps) {
  const hint = resolveHint(definition);

  const Field = hintFieldMap[hint] ?? typeFieldMap[definition.type] ?? StringField;
  return <Field definition={definition} value={value} onChange={onChange} error={error} />;
}

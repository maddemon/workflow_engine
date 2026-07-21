import type { ParameterDefinition, ValidationRuleDto } from '../types/workflow.ts';

export interface ValidationError {
  name: string;
  message: string;
}

/**
 * Validate parameters against their definitions.
 * Returns a flat record of field name → error message.
 */
export function validateParameters(
  parameters: Record<string, unknown>,
  definitions: ParameterDefinition[],
): Record<string, string> {
  const errors: Record<string, string> = {};

  for (const def of definitions) {
    const value = parameters[def.name];

    if (def.required && (value === undefined || value === null || value === '')) {
      errors[def.name] = `${def.displayName} is required`;
      continue;
    }

    if (value === undefined || value === null || value === '') continue;

    for (const rule of def.validationRules) {
      applyRule(def, rule, value, errors);
    }
  }

  return errors;
}

/**
 * 按对象规则校验单个值。rule.type 大小写不敏感；
 * rule.errorMessage 存在时优先作为错误提示，否则按类型生成默认提示。
 */
function applyRule(
  def: ParameterDefinition,
  rule: ValidationRuleDto,
  value: unknown,
  errors: Record<string, string>,
): void {
  const type = rule.type.toLowerCase();

  if (type === 'minlength') {
    const min = Number(rule.value);
    if (Number.isNaN(min)) return;
    if (typeof value === 'string' && value.length < min) {
      errors[def.name] = rule.errorMessage ?? `${def.displayName} must be at least ${min} characters`;
    }
  } else if (type === 'maxlength') {
    const max = Number(rule.value);
    if (Number.isNaN(max)) return;
    if (typeof value === 'string' && value.length > max) {
      errors[def.name] = rule.errorMessage ?? `${def.displayName} must be at most ${max} characters`;
    }
  } else if (type === 'min') {
    const min = Number(rule.value);
    if (Number.isNaN(min)) return;
    if (typeof value === 'number' && value < min) {
      errors[def.name] = rule.errorMessage ?? `${def.displayName} must be at least ${min}`;
    }
  } else if (type === 'max') {
    const max = Number(rule.value);
    if (Number.isNaN(max)) return;
    if (typeof value === 'number' && value > max) {
      errors[def.name] = rule.errorMessage ?? `${def.displayName} must be at most ${max}`;
    }
  } else if (type === 'pattern') {
    const pattern = String(rule.value);
    // 空正则或非法正则直接跳过，避免校验失效或抛异常（R11）。
    if (!pattern) return;
    let regex: RegExp;
    try {
      regex = new RegExp(pattern);
    } catch {
      return;
    }
    if (typeof value === 'string' && !regex.test(value)) {
      errors[def.name] = rule.errorMessage ?? `${def.displayName} format is invalid`;
    }
  }
}

/**
 * Validate parameters and return as array of { name, message } objects.
 */
export function validateParametersAsArray(
  parameters: Record<string, unknown>,
  definitions: ParameterDefinition[],
): ValidationError[] {
  const record = validateParameters(parameters, definitions);
  return Object.entries(record).map(([name, message]) => ({ name, message }));
}

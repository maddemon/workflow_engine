import type { ParameterDefinition } from '../types/workflow.ts';

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
      const ruleLower = rule.toLowerCase();

      if (ruleLower.startsWith('minlength:')) {
        const min = parseInt(rule.split(':')[1], 10);
        if (typeof value === 'string' && value.length < min) {
          errors[def.name] = `${def.displayName} must be at least ${min} characters`;
        }
      } else if (ruleLower.startsWith('maxlength:')) {
        const max = parseInt(rule.split(':')[1], 10);
        if (typeof value === 'string' && value.length > max) {
          errors[def.name] = `${def.displayName} must be at most ${max} characters`;
        }
      } else if (ruleLower.startsWith('min:')) {
        const min = parseFloat(rule.split(':')[1]);
        if (typeof value === 'number' && value < min) {
          errors[def.name] = `${def.displayName} must be at least ${min}`;
        }
      } else if (ruleLower.startsWith('max:')) {
        const max = parseFloat(rule.split(':')[1]);
        if (typeof value === 'number' && value > max) {
          errors[def.name] = `${def.displayName} must be at most ${max}`;
        }
      } else if (ruleLower.startsWith('pattern:')) {
        const pattern = rule.split(':').slice(1).join(':');
        if (typeof value === 'string' && !new RegExp(pattern).test(value)) {
          errors[def.name] = `${def.displayName} format is invalid`;
        }
      }
    }
  }

  return errors;
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

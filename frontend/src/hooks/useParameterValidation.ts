import { useMemo } from 'react';
import type { ParameterDefinition } from '../types/workflow.ts';

interface ValidationError {
  name: string;
  message: string;
}

export function useParameterValidation(
  definitions: ParameterDefinition[],
  values: Record<string, unknown>,
) {
  const errors = useMemo(() => {
    const result: ValidationError[] = [];

    for (const def of definitions) {
      const value = values[def.name];

      if (def.required && (value === undefined || value === null || value === '')) {
        result.push({ name: def.name, message: `${def.displayName} is required` });
        continue;
      }

      if (value === undefined || value === null || value === '') continue;

      for (const rule of def.validationRules) {
        const ruleLower = rule.toLowerCase();

        if (ruleLower.startsWith('minlength:')) {
          const min = parseInt(rule.split(':')[1], 10);
          if (typeof value === 'string' && value.length < min) {
            result.push({ name: def.name, message: `${def.displayName} must be at least ${min} characters` });
          }
        } else if (ruleLower.startsWith('maxlength:')) {
          const max = parseInt(rule.split(':')[1], 10);
          if (typeof value === 'string' && value.length > max) {
            result.push({ name: def.name, message: `${def.displayName} must be at most ${max} characters` });
          }
        } else if (ruleLower.startsWith('min:')) {
          const min = parseFloat(rule.split(':')[1]);
          if (typeof value === 'number' && value < min) {
            result.push({ name: def.name, message: `${def.displayName} must be at least ${min}` });
          }
        } else if (ruleLower.startsWith('max:')) {
          const max = parseFloat(rule.split(':')[1]);
          if (typeof value === 'number' && value > max) {
            result.push({ name: def.name, message: `${def.displayName} must be at most ${max}` });
          }
        } else if (ruleLower === 'pattern' && rule.includes(':')) {
          const pattern = rule.split(':').slice(1).join(':');
          if (typeof value === 'string' && !new RegExp(pattern).test(value)) {
            result.push({ name: def.name, message: `${def.displayName} format is invalid` });
          }
        }
      }
    }

    return result;
  }, [definitions, values]);

  const isValid = errors.length === 0;

  const getError = (name: string): string | undefined =>
    errors.find((e) => e.name === name)?.message;

  return { errors, isValid, getError };
}

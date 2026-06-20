import { useMemo } from 'react';
import type { ParameterDefinition } from '../types/workflow.ts';
import { validateParametersAsArray } from '../utils/validateParameters.ts';
import type { ValidationError } from '../utils/validateParameters.ts';

export function useParameterValidation(
  definitions: ParameterDefinition[],
  values: Record<string, unknown>,
) {
  const errors = useMemo(
    () => validateParametersAsArray(values, definitions),
    [definitions, values],
  );

  const isValid = errors.length === 0;

  const getError = (name: string): string | undefined =>
    errors.find((e) => e.name === name)?.message;

  return { errors, isValid, getError };
}

export type { ValidationError };

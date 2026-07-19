import { describe, it, expect } from 'vitest';
import { renderHook } from '@testing-library/react';
import { useParameterValidation } from '../useParameterValidation.ts';
import type { ParameterDefinition } from '../../types/workflow.ts';

function makeDefinition(required: boolean): ParameterDefinition {
  return {
    name: 'url',
    displayName: 'URL',
    type: 'String',
    defaultValue: '',
    required,
    validationRules: [],
    displayRule: null,
    credentialType: null,
    options: [],
  };
}

describe('useParameterValidation', () => {
  it('emptyDefinitions_returnsValid', () => {
    const { result } = renderHook(() => useParameterValidation([], {}));
    expect(result.current.isValid).toBe(true);
    expect(result.current.errors).toHaveLength(0);
    expect(result.current.getError('x')).toBeUndefined();
  });

  it('requiredFieldMissing_returnsInvalidWithError', () => {
    const { result } = renderHook(() => useParameterValidation([makeDefinition(true)], {}));
    expect(result.current.isValid).toBe(false);
    expect(result.current.errors.length).toBeGreaterThan(0);
    expect(result.current.getError('url')).toBeDefined();
  });

  it('requiredFieldProvided_returnsValid', () => {
    const { result } = renderHook(() => useParameterValidation([makeDefinition(true)], { url: 'http://x' }));
    expect(result.current.isValid).toBe(true);
    expect(result.current.getError('url')).toBeUndefined();
  });

  it('memoizesResultForSameInputs', () => {
    const defs = [makeDefinition(false)];
    const values = { url: 'v' };
    const { result, rerender } = renderHook(({ d, v }) => useParameterValidation(d, v), {
      initialProps: { d: defs, v: values },
    });
    const first = result.current.errors;
    rerender({ d: defs, v: values });
    expect(result.current.errors).toBe(first);
  });
});

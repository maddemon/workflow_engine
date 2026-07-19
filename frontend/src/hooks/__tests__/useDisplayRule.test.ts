import { describe, it, expect } from 'vitest';
import { renderHook } from '@testing-library/react';
import { useDisplayRule } from '../useDisplayRule.ts';
import type { ParameterDefinition } from '../../types/workflow.ts';

function makeDef(displayRule?: ParameterDefinition['displayRule']): ParameterDefinition {
  return {
    name: 'field',
    displayName: 'Field',
    type: 'String',
    required: false,
    defaultValue: undefined,
    validationRules: [],
    displayRule: displayRule ?? null,
    credentialType: null,
    options: [],
  } as ParameterDefinition;
}

describe('useDisplayRule', () => {
  it('returns true when displayRule is absent', () => {
    const { result } = renderHook(() => useDisplayRule({}));
    expect(result.current.isVisible(makeDef())).toBe(true);
  });

  it('returns true when condition or dependencies are empty', () => {
    const { result } = renderHook(() => useDisplayRule({ method: 'GET' }));
    expect(result.current.isVisible(makeDef({ condition: '', dependencies: [] }))).toBe(true);
    expect(result.current.isVisible(makeDef({ condition: "{{ method }} == 'POST'", dependencies: [] }))).toBe(true);
  });

  it('evaluates == comparison', () => {
    const { result } = renderHook(() => useDisplayRule({ method: 'POST' }));
    const def = makeDef({ condition: "{{ parameter.method }} == 'POST'", dependencies: ['method'] });
    expect(result.current.isVisible(def)).toBe(true);

    const result2 = renderHook(() => useDisplayRule({ method: 'GET' })).result;
    expect(result2.current.isVisible(def)).toBe(false);
  });

  it('evaluates != comparison', () => {
    const { result } = renderHook(() => useDisplayRule({ env: 'prod' }));
    const def = makeDef({ condition: "{{ $parameter.env }} != 'dev'", dependencies: ['env'] });
    expect(result.current.isVisible(def)).toBe(true);

    const result2 = renderHook(() => useDisplayRule({ env: 'dev' })).result;
    expect(result2.current.isVisible(def)).toBe(false);
  });

  it('evaluates || combined conditions', () => {
    const { result } = renderHook(() => useDisplayRule({ method: 'DELETE' }));
    const def = makeDef({ condition: "{{ parameter.method }} == 'POST' || {{ parameter.method }} == 'DELETE'", dependencies: ['method'] });
    expect(result.current.isVisible(def)).toBe(true);
  });

  it('evaluates && combined conditions', () => {
    const { result } = renderHook(() => useDisplayRule({ method: 'POST', auth: true }));
    const def = makeDef({ condition: "{{ parameter.method }} == 'POST' && {{ parameter.auth }} == 'true'", dependencies: ['method', 'auth'] });
    expect(result.current.isVisible(def)).toBe(true);
  });

  it('returns true for unparseable conditions', () => {
    const { result } = renderHook(() => useDisplayRule({}));
    const def = makeDef({ condition: 'invalid > expression', dependencies: ['x'] });
    expect(result.current.isVisible(def)).toBe(true);
  });

  it('returns false when nested dependency value is missing', () => {
    const { result } = renderHook(() => useDisplayRule({}));
    const def = makeDef({ condition: "{{ parameter.nested.deep }} == 'v'", dependencies: ['nested'] });
    expect(result.current.isVisible(def)).toBe(false);
  });
});

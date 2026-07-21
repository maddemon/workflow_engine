import { describe, it, expect } from 'vitest';
import { validateParameters } from '../validateParameters';
import type { ParameterDefinition } from '../../types/workflow';

function makeDef(overrides: Partial<ParameterDefinition> = {}): ParameterDefinition {
  return {
    name: 'test',
    displayName: 'Test',
    type: 'String',
    required: false,
    defaultValue: '',
    validationRules: [],
    options: [],
    description: null,
    hint: null,
    displayRule: null,
    credentialType: null,
    resourceType: null,
    itemDefinition: null,
    ...overrides,
  };
}

describe('validateParameters', () => {
  it('returns empty for valid values', () => {
    const defs = [makeDef({ name: 'url', required: true })];
    const result = validateParameters({ url: 'https://example.com' }, defs);
    expect(Object.keys(result)).toHaveLength(0);
  });

  it('returns error for missing required field', () => {
    const defs = [makeDef({ name: 'url', displayName: 'URL', required: true })];
    const result = validateParameters({ url: '' }, defs);
    expect(result['url']).toBe('URL is required');
  });

  it('skips validation for empty non-required fields', () => {
    const defs = [makeDef({ name: 'url', validationRules: [{ type: 'minLength', value: 5 }] })];
    const result = validateParameters({ url: '' }, defs);
    expect(Object.keys(result)).toHaveLength(0);
  });

  it('validates minLength rule', () => {
    const defs = [makeDef({ name: 'code', displayName: 'Code', validationRules: [{ type: 'minLength', value: 3 }] })];
    const result = validateParameters({ code: 'ab' }, defs);
    expect(result['code']).toContain('at least 3');
  });

  it('validates maxLength rule', () => {
    const defs = [makeDef({ name: 'code', displayName: 'Code', validationRules: [{ type: 'maxLength', value: 5 }] })];
    const result = validateParameters({ code: 'abcdef' }, defs);
    expect(result['code']).toContain('at most 5');
  });

  it('validates min rule for numbers', () => {
    const defs = [makeDef({ name: 'count', type: 'Number', displayName: 'Count', validationRules: [{ type: 'min', value: 1 }] })];
    const result = validateParameters({ count: 0 }, defs);
    expect(result['count']).toContain('at least 1');
  });

  it('validates max rule for numbers', () => {
    const defs = [makeDef({ name: 'count', type: 'Number', displayName: 'Count', validationRules: [{ type: 'max', value: 100 }] })];
    const result = validateParameters({ count: 200 }, defs);
    expect(result['count']).toContain('at most 100');
  });

  it('validates pattern rule', () => {
    const defs = [makeDef({ name: 'email', displayName: 'Email', validationRules: [{ type: 'pattern', value: '^\\S+@\\S+$' }] })];
    const result = validateParameters({ email: 'invalid' }, defs);
    expect(result['email']).toContain('format is invalid');
  });

  it('returns no error for valid pattern', () => {
    const defs = [makeDef({ name: 'email', displayName: 'Email', validationRules: [{ type: 'pattern', value: '^\\S+@\\S+$' }] })];
    const result = validateParameters({ email: 'test@example.com' }, defs);
    expect(Object.keys(result)).toHaveLength(0);
  });

  describe('object validationRules parsing (#9)', () => {
    it('uses custom errorMessage when provided', () => {
      const defs = [makeDef({
        name: 'code',
        displayName: 'Code',
        validationRules: [{ type: 'minLength', value: 3, errorMessage: 'Too short' }],
      })];
      const result = validateParameters({ code: 'ab' }, defs);
      expect(result['code']).toBe('Too short');
    });

    it('parses rule type case-insensitively', () => {
      const defs = [makeDef({
        name: 'code',
        displayName: 'Code',
        validationRules: [{ type: 'MINLENGTH', value: 3 }],
      })];
      const result = validateParameters({ code: 'ab' }, defs);
      expect(result['code']).toContain('at least 3');
    });

    it('returns no error when value satisfies the rule (boundary)', () => {
      const defs = [makeDef({
        name: 'code',
        displayName: 'Code',
        validationRules: [{ type: 'minLength', value: 3 }],
      })];
      const result = validateParameters({ code: 'abc' }, defs);
      expect(Object.keys(result)).toHaveLength(0);
    });

    it('ignores rule with non-numeric value and does not throw', () => {
      const defs = [makeDef({
        name: 'code',
        displayName: 'Code',
        validationRules: [{ type: 'minLength', value: true }],
      })];
      const result = validateParameters({ code: 'ab' }, defs);
      expect(Object.keys(result)).toHaveLength(0);
    });

    it('ignores unsupported rule types', () => {
      const defs = [makeDef({
        name: 'code',
        displayName: 'Code',
        validationRules: [{ type: 'unknown', value: 3 }],
      })];
      const result = validateParameters({ code: 'ab' }, defs);
      expect(Object.keys(result)).toHaveLength(0);
    });
  });
});

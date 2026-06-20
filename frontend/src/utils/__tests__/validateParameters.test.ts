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
    const defs = [makeDef({ name: 'url', validationRules: ['minlength:5'] })];
    const result = validateParameters({ url: '' }, defs);
    expect(Object.keys(result)).toHaveLength(0);
  });

  it('validates minlength rule', () => {
    const defs = [makeDef({ name: 'code', displayName: 'Code', validationRules: ['minlength:3'] })];
    const result = validateParameters({ code: 'ab' }, defs);
    expect(result['code']).toContain('at least 3');
  });

  it('validates maxlength rule', () => {
    const defs = [makeDef({ name: 'code', displayName: 'Code', validationRules: ['maxlength:5'] })];
    const result = validateParameters({ code: 'abcdef' }, defs);
    expect(result['code']).toContain('at most 5');
  });

  it('validates min rule for numbers', () => {
    const defs = [makeDef({ name: 'count', type: 'Number', displayName: 'Count', validationRules: ['min:1'] })];
    const result = validateParameters({ count: 0 }, defs);
    expect(result['count']).toContain('at least 1');
  });

  it('validates max rule for numbers', () => {
    const defs = [makeDef({ name: 'count', type: 'Number', displayName: 'Count', validationRules: ['max:100'] })];
    const result = validateParameters({ count: 200 }, defs);
    expect(result['count']).toContain('at most 100');
  });

  it('validates pattern rule', () => {
    const defs = [makeDef({ name: 'email', displayName: 'Email', validationRules: ['pattern:^\\S+@\\S+$'] })];
    const result = validateParameters({ email: 'invalid' }, defs);
    expect(result['email']).toContain('format is invalid');
  });

  it('returns no error for valid pattern', () => {
    const defs = [makeDef({ name: 'email', displayName: 'Email', validationRules: ['pattern:^\\S+@\\S+$'] })];
    const result = validateParameters({ email: 'test@example.com' }, defs);
    expect(Object.keys(result)).toHaveLength(0);
  });
});

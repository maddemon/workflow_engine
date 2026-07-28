import { describe, it, expect } from 'vitest';
import {
  defaultCredentialTypeOptions,
  type CredentialTypeDefinition,
} from '../workflow.ts';

describe('defaultCredentialTypeOptions', () => {
  it('exists and is typed as CredentialTypeDefinition[]', () => {
    expect(Array.isArray(defaultCredentialTypeOptions)).toBe(true);
    expect((defaultCredentialTypeOptions as CredentialTypeDefinition[]).length).toBeGreaterThan(0);
  });

  it('contains exactly the four canonical credential types', () => {
    const names = defaultCredentialTypeOptions.map((o) => o.name).sort();
    expect(names).toEqual(['apiKey', 'basicAuth', 'database', 'oauth2']);
  });

  it('does NOT include the invalid connectionString type', () => {
    const names = defaultCredentialTypeOptions.map((o) => o.name);
    expect(names).not.toContain('connectionString');
  });

  it('matches the CredentialTypeDefinition shape (name/displayName/fields)', () => {
    for (const o of defaultCredentialTypeOptions) {
      expect(typeof o.name).toBe('string');
      expect(typeof o.displayName).toBe('string');
      expect(Array.isArray(o.fields)).toBe(true);
    }
  });
});

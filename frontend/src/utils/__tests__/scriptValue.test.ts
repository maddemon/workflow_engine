import { describe, it, expect } from 'vitest';
import { extractScriptSource } from '../scriptValue.ts';

describe('extractScriptSource', () => {
  it('returns empty string for null/undefined', () => {
    expect(extractScriptSource(null)).toBe('');
    expect(extractScriptSource(undefined)).toBe('');
  });

  it('returns the raw string for plain string values', () => {
    expect(extractScriptSource('https://api.example.com')).toBe('https://api.example.com');
  });

  it('extracts source from Script object', () => {
    expect(extractScriptSource({ source: "'https://api.example.com'" })).toBe("'https://api.example.com'");
  });

  it('returns empty string when Script object has no source', () => {
    expect(extractScriptSource({})).toBe('');
  });

  it('returns empty string for non-string source', () => {
    expect(extractScriptSource({ source: 123 })).toBe('');
  });
});

import { describe, it, expect } from 'vitest';
import { normalizeLayoutDirection } from '../workflowLayout.ts';

describe('normalizeLayoutDirection', () => {
  it('normalizeLayoutDirection - null - returns vertical', () => {
    expect(normalizeLayoutDirection(null)).toBe('vertical');
  });

  it('normalizeLayoutDirection - undefined - returns vertical', () => {
    expect(normalizeLayoutDirection(undefined)).toBe('vertical');
  });

  it('normalizeLayoutDirection - empty string - returns vertical', () => {
    expect(normalizeLayoutDirection('')).toBe('vertical');
  });

  it('normalizeLayoutDirection - horizontal - returns horizontal', () => {
    expect(normalizeLayoutDirection('horizontal')).toBe('horizontal');
  });

  it('normalizeLayoutDirection - vertical - returns vertical', () => {
    expect(normalizeLayoutDirection('vertical')).toBe('vertical');
  });

  it('normalizeLayoutDirection - unexpected string (HORIZONTAL) - returns vertical', () => {
    expect(normalizeLayoutDirection('HORIZONTAL')).toBe('vertical');
  });

  it('normalizeLayoutDirection - garbage (diagonal) - returns vertical', () => {
    expect(normalizeLayoutDirection('diagonal')).toBe('vertical');
  });
});

import { describe, it, expect, vi } from 'vitest';
import { renderHook } from '@testing-library/react';
import { useRoles } from '../useRoles';

// Mock useAuth
vi.mock('../AuthContext.tsx', () => ({
  useAuth: vi.fn(),
}));

import { useAuth } from '../AuthContext.tsx';
const mockedUseAuth = vi.mocked(useAuth);

function mockAuth(overrides: Partial<{ roles: string[]; hasRole: (r: string) => boolean }>) {
  return {
    user: null,
    isAuthenticated: false,
    isLoading: false,
    roles: overrides.roles ?? [],
    hasRole: overrides.hasRole ?? (() => false),
    login: vi.fn(),
    logout: vi.fn(),
  };
}

describe('useRoles', () => {
  it('returns roles from useAuth', () => {
    mockedUseAuth.mockReturnValue(mockAuth({
      roles: ['Admin', 'Editor'],
      hasRole: (r: string) => ['Admin', 'Editor'].includes(r),
    }));

    const { result } = renderHook(() => useRoles());
    expect(result.current.roles).toEqual(['Admin', 'Editor']);
  });

  it('hasRole returns true when role is present', () => {
    mockedUseAuth.mockReturnValue(mockAuth({
      roles: ['Admin'],
      hasRole: (r: string) => r === 'Admin',
    }));

    const { result } = renderHook(() => useRoles());
    expect(result.current.hasRole('Admin')).toBe(true);
  });

  it('hasRole returns false when role is absent', () => {
    mockedUseAuth.mockReturnValue(mockAuth({
      roles: ['Viewer'],
      hasRole: (r: string) => r === 'Viewer',
    }));

    const { result } = renderHook(() => useRoles());
    expect(result.current.hasRole('Admin')).toBe(false);
  });

  it('hasRole returns false for empty roles', () => {
    mockedUseAuth.mockReturnValue(mockAuth({
      roles: [],
      hasRole: () => false,
    }));

    const { result } = renderHook(() => useRoles());
    expect(result.current.hasRole('Admin')).toBe(false);
  });
});

import { describe, it, expect, vi } from 'vitest';
import { screen } from '@testing-library/react';
import { renderWithProvider } from '../../../test-utils.tsx';
import { RequireRole } from '../RequireRole';

// Mock the useAuth hook
vi.mock('../../../hooks/AuthContext.tsx', () => ({
  useAuth: vi.fn(),
}));

vi.mock('../../../hooks/useRoles.ts', () => ({
  useRoles: vi.fn(),
}));

import { useRoles } from '../../../hooks/useRoles.ts';
const mockedUseRoles = vi.mocked(useRoles);

describe('RequireRole', () => {
  it('renders children when hasRole returns true', () => {
    mockedUseRoles.mockReturnValue({ hasRole: () => true, roles: ['Admin'] });
    renderWithProvider(<RequireRole role="Admin"><div>Admin content</div></RequireRole>);
    expect(screen.getByText('Admin content')).toBeDefined();
  });

  it('does not render children when hasRole returns false', () => {
    mockedUseRoles.mockReturnValue({ hasRole: () => false, roles: [] });
    renderWithProvider(<RequireRole role="Admin"><div>Admin content</div></RequireRole>);
    expect(screen.queryByText('Admin content')).toBeNull();
  });

  it('shows default permission denied message when no fallback', () => {
    mockedUseRoles.mockReturnValue({ hasRole: () => false, roles: [] });
    renderWithProvider(<RequireRole role="Admin"><div>Admin content</div></RequireRole>);
    expect(screen.getByTestId('permission-denied')).toBeDefined();
  });

  it('renders custom fallback when provided', () => {
    mockedUseRoles.mockReturnValue({ hasRole: () => false, roles: [] });
    renderWithProvider(<RequireRole role="Admin" fallback={<div data-testid="custom-denied">Custom denied</div>}><div>Admin content</div></RequireRole>);
    expect(screen.getByTestId('custom-denied')).toBeDefined();
  });
});

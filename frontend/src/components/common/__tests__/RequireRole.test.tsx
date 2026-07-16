import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
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
    render(<RequireRole role="Admin"><div>Admin content</div></RequireRole>);
    expect(screen.getByText('Admin content')).toBeDefined();
  });

  it('does not render children when hasRole returns false', () => {
    mockedUseRoles.mockReturnValue({ hasRole: () => false, roles: [] });
    render(<RequireRole role="Admin"><div>Admin content</div></RequireRole>);
    expect(screen.queryByText('Admin content')).toBeNull();
  });

  it('shows default permission denied message when no fallback', () => {
    mockedUseRoles.mockReturnValue({ hasRole: () => false, roles: [] });
    render(<RequireRole role="Admin"><div>Admin content</div></RequireRole>);
    expect(screen.getByText(/do not have permission/i)).toBeDefined();
  });

  it('renders custom fallback when provided', () => {
    mockedUseRoles.mockReturnValue({ hasRole: () => false, roles: [] });
    render(<RequireRole role="Admin" fallback={<div>Custom denied</div>}><div>Admin content</div></RequireRole>);
    expect(screen.getByText('Custom denied')).toBeDefined();
  });
});
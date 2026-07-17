import { describe, it, expect, vi } from 'vitest';
import { screen } from '@testing-library/react';
import { renderWithProvider } from '../../test-utils.tsx';
import { AdminUsersPage } from '../AdminUsersPage';

vi.mock('../../hooks/AuthContext.tsx', () => ({
  useAuth: vi.fn(),
}));

vi.mock('../../hooks/useRoles.ts', () => ({
  useRoles: vi.fn(),
}));

import { useAuth } from '../../hooks/AuthContext.tsx';
const mockedUseAuth = vi.mocked(useAuth);
import { useRoles } from '../../hooks/useRoles.ts';
const mockedUseRoles = vi.mocked(useRoles);

describe('AdminUsersPage', () => {
  it('renders title and info banner', () => {
    mockedUseRoles.mockReturnValue({ hasRole: () => true, roles: ['Admin'] });
    mockedUseAuth.mockReturnValue({
      user: { id: '1', email: 'test@test.com', userName: 'test', displayName: 'Test User', roles: ['Admin'], isActive: true, createdAt: '2024-01-01', updatedAt: '2024-01-01' },
      isAuthenticated: true,
      isLoading: false,
      roles: ['Admin'],
      hasRole: () => true,
      login: vi.fn(),
      logout: vi.fn(),
    });

    renderWithProvider(<AdminUsersPage />);
    expect(screen.getByRole('heading', { name: /user/i })).toBeDefined();
    expect(screen.getByText('Test User')).toBeDefined();
  });

  it('shows Manage Roles button for admin user', () => {
    mockedUseRoles.mockReturnValue({ hasRole: () => true, roles: ['Admin'] });
    mockedUseAuth.mockReturnValue({
      user: { id: '1', email: 'admin@test.com', userName: 'admin', displayName: 'Admin', roles: ['Admin'], isActive: true, createdAt: '2024-01-01', updatedAt: '2024-01-01' },
      isAuthenticated: true,
      isLoading: false,
      roles: ['Admin'],
      hasRole: () => true,
      login: vi.fn(),
      logout: vi.fn(),
    });

    renderWithProvider(<AdminUsersPage />);
    expect(screen.getByRole('button', { name: /manage roles/i })).toBeDefined();
  });
});

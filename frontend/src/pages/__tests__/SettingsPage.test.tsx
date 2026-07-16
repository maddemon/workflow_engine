import { describe, it, expect, vi } from 'vitest';
import { screen } from '@testing-library/react';
import { renderWithProvider } from '../../test-utils.tsx';
import { SettingsPage } from '../SettingsPage';

vi.mock('../../hooks/AuthContext.tsx', () => ({
  useAuth: vi.fn(),
}));

vi.mock('../../hooks/useRoles.ts', () => ({
  useRoles: vi.fn(),
}));

vi.mock('../../services/api.ts', () => ({
  listApiKeys: vi.fn().mockResolvedValue([]),
  createApiKey: vi.fn(),
  revokeApiKey: vi.fn(),
}));

import { useAuth } from '../../hooks/AuthContext.tsx';
const mockedUseAuth = vi.mocked(useAuth);
import { useRoles } from '../../hooks/useRoles.ts';
const mockedUseRoles = vi.mocked(useRoles);

describe('SettingsPage', () => {
  it('renders settings sections', () => {
    mockedUseRoles.mockReturnValue({ hasRole: () => false, roles: ['Editor'] });
    mockedUseAuth.mockReturnValue({
      user: { id: '1', email: 'user@test.com', userName: 'user', displayName: 'User', roles: ['Editor'], isActive: true, createdAt: '2024-01-01', updatedAt: '2024-01-01' },
      isAuthenticated: true,
      isLoading: false,
      roles: ['Editor'],
      hasRole: () => false,
      login: vi.fn(),
      logout: vi.fn(),
    });

    renderWithProvider(<SettingsPage />);
    expect(screen.getByText('Settings')).toBeDefined();
  });
});

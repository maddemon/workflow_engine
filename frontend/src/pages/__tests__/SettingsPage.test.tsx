import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen, fireEvent, waitFor } from '@testing-library/react';
import { renderWithProvider } from '../../test-utils.tsx';
import { SettingsPage } from '../SettingsPage';
import type { ApiKeyDto } from '../../services/api.ts';
import type { CreateApiKeyResult } from '../../types/workflow.ts';

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

vi.mock('@mantine/notifications', () => ({
  notifications: { show: vi.fn() },
}));

import { useAuth } from '../../hooks/AuthContext.tsx';
const mockedUseAuth = vi.mocked(useAuth);
import { useRoles } from '../../hooks/useRoles.ts';
const mockedUseRoles = vi.mocked(useRoles);
import { listApiKeys, createApiKey, revokeApiKey } from '../../services/api.ts';
const mockedListApiKeys = vi.mocked(listApiKeys);
const mockedCreateApiKey = vi.mocked(createApiKey);
const mockedRevokeApiKey = vi.mocked(revokeApiKey);

function mockUser() {
  return {
    user: { id: '1', email: 'user@test.com', userName: 'user', displayName: 'User', roles: ['Editor'], isActive: true, createdAt: '2024-01-01', updatedAt: '2024-01-01' },
    isAuthenticated: true,
    isLoading: false,
    roles: ['Editor'],
    hasRole: () => false,
    login: vi.fn(),
    logout: vi.fn(),
  };
}

describe('SettingsPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockedUseRoles.mockReturnValue({ hasRole: () => false, roles: ['Editor'] });
    mockedUseAuth.mockReturnValue(mockUser());
  });

  it('renders settings sections', () => {
    renderWithProvider(<SettingsPage />);
    expect(screen.getByRole('heading', { name: /settings/i })).toBeDefined();
  });

  it('lists api keys and shows active status', async () => {
    const keys: ApiKeyDto[] = [
      { id: 'k1', name: 'Dev', prefix: 'fe_dev', createdAt: '2024-01-01', expiresAt: null, revokedAt: null },
    ];
    mockedListApiKeys.mockResolvedValue(keys);

    renderWithProvider(<SettingsPage />);

    await waitFor(() => {
      expect(screen.getByText('Dev')).toBeDefined();
    });
    expect(screen.getByText('Active')).toBeDefined();
  });

  it('creates a new api key and displays it', async () => {
    mockedListApiKeys.mockResolvedValue([]);
    const created: CreateApiKeyResult = { id: 'k2', name: 'CI', prefix: 'fe_ci', expiresAt: null, key: 'fe_ci_secret' };
    mockedCreateApiKey.mockResolvedValue(created);

    renderWithProvider(<SettingsPage />);

    fireEvent.click(screen.getByRole('button', { name: /create api key/i }));
    await waitFor(() => {
      expect(screen.getByPlaceholderText(/e\.g\. my api key/i)).toBeDefined();
    });
    fireEvent.change(screen.getByPlaceholderText(/e\.g\. my api key/i), { target: { value: 'CI' } });
    fireEvent.click(screen.getByRole('button', { name: /^create$/i }));

    await waitFor(() => {
      expect(mockedCreateApiKey).toHaveBeenCalledWith('CI', null);
    });
    await waitFor(() => {
      expect(screen.getByText('fe_ci_secret')).toBeDefined();
    });
  });

  it('revokes an api key after confirmation', async () => {
    const keys: ApiKeyDto[] = [
      { id: 'k1', name: 'Dev', prefix: 'fe_dev', createdAt: '2024-01-01', expiresAt: null, revokedAt: null },
    ];
    mockedListApiKeys.mockResolvedValue(keys);
    mockedRevokeApiKey.mockResolvedValue(undefined);

    renderWithProvider(<SettingsPage />);

    await waitFor(() => {
      expect(screen.getByText('Dev')).toBeDefined();
    });

    const rows = screen.getAllByRole('row');
    const dataRow = rows.find((r) => r.textContent?.includes('Dev'));
    const revokeButton = dataRow?.querySelector('button');
    expect(revokeButton).not.toBeNull();
    fireEvent.click(revokeButton!);

    await waitFor(() => {
      expect(screen.getByRole('button', { name: /revoke$/i })).toBeDefined();
    });
    fireEvent.click(screen.getByRole('button', { name: /revoke$/i }));

    await waitFor(() => {
      expect(mockedRevokeApiKey).toHaveBeenCalledWith('k1');
    });
  });
});

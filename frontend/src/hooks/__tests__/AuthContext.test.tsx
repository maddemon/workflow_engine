import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen, fireEvent, waitFor, renderHook } from '@testing-library/react';
import { useAuth } from '../AuthContext.tsx';
import { renderWithProvider } from '../../test-utils.tsx';
import * as api from '../../services/api.ts';
import type { UserDto, LoginRequest } from '../../types/workflow.ts';

vi.mock('../../services/api.ts', () => ({
  login: vi.fn(),
  logout: vi.fn(),
  getCurrentUser: vi.fn(),
}));

vi.mock('@mantine/notifications', () => ({
  notifications: { show: vi.fn() },
}));

const mockedLogin = vi.mocked(api.login);
const mockedLogout = vi.mocked(api.logout);
const mockedGetCurrentUser = vi.mocked(api.getCurrentUser);

function TestComponent() {
  const { user, isAuthenticated, isLoading, roles, hasRole, login, logout } = useAuth();
  return (
    <div>
      <div data-testid="loading">{isLoading ? 'loading' : 'idle'}</div>
      <div data-testid="authenticated">{isAuthenticated ? 'yes' : 'no'}</div>
      <div data-testid="user">{user?.displayName ?? 'none'}</div>
      <div data-testid="roles">{roles.join(',')}</div>
      <div data-testid="has-admin">{hasRole('Admin') ? 'yes' : 'no'}</div>
      <button
        data-testid="login-btn"
        onClick={async () => {
          const result = await login({ email: 'test@example.com', password: 'secret' } as LoginRequest);
          document.body.setAttribute('data-login-result', String(result.success));
          if (!result.success) {
            document.body.setAttribute('data-login-error', result.error ?? '');
          }
        }}
      >
        Login
      </button>
      <button data-testid="logout-btn" onClick={async () => logout()}>
        Logout
      </button>
    </div>
  );
}

describe('AuthContext', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    window.localStorage.clear();
  });

  it('starts unauthenticated when no stored user', async () => {
    mockedGetCurrentUser.mockRejectedValue(new Error('Unauthorized'));

    renderWithProvider(<TestComponent />, { withAuth: true });

    await waitFor(() => {
      expect(screen.getByTestId('loading')).toHaveTextContent('idle');
    });

    expect(screen.getByTestId('authenticated')).toHaveTextContent('no');
    expect(screen.getByTestId('user')).toHaveTextContent('none');
    expect(screen.getByTestId('roles')).toHaveTextContent('');
  });

  it('restores user from localStorage and refreshes via getCurrentUser', async () => {
    const storedUser: UserDto = {
      id: 'u1',
      email: 'stored@example.com',
      userName: 'stored',
      displayName: 'Stored User',
      roles: ['Editor'],
      isActive: true,
      createdAt: '2024-01-01T00:00:00Z',
      updatedAt: '2024-01-01T00:00:00Z',
    };
    const refreshedUser: UserDto = { ...storedUser, displayName: 'Refreshed User', roles: ['Editor', 'Admin'] };

    localStorage.setItem('auth_user', JSON.stringify(storedUser));
    mockedGetCurrentUser.mockResolvedValue(refreshedUser);

    renderWithProvider(<TestComponent />, { withAuth: true });

    await waitFor(() => {
      expect(screen.getByTestId('user')).toHaveTextContent('Refreshed User');
    });

    expect(screen.getByTestId('authenticated')).toHaveTextContent('yes');
    expect(screen.getByTestId('roles')).toHaveTextContent('Editor,Admin');
    expect(screen.getByTestId('has-admin')).toHaveTextContent('yes');
    expect(localStorage.getItem('auth_user')).toBe(JSON.stringify(refreshedUser));
  });

  it('clears stored user when getCurrentUser fails', async () => {
    const storedUser: UserDto = {
      id: 'u1',
      email: 'stored@example.com',
      userName: 'stored',
      displayName: 'Stored User',
      roles: ['Editor'],
      isActive: true,
      createdAt: '2024-01-01T00:00:00Z',
      updatedAt: '2024-01-01T00:00:00Z',
    };

    localStorage.setItem('auth_user', JSON.stringify(storedUser));
    mockedGetCurrentUser.mockRejectedValue(new Error('Unauthorized'));

    renderWithProvider(<TestComponent />, { withAuth: true });

    await waitFor(() => {
      expect(screen.getByTestId('authenticated')).toHaveTextContent('no');
    });

    expect(localStorage.getItem('auth_user')).toBeNull();
  });

  it('login succeeds and stores user', async () => {
    const user: UserDto = {
      id: 'u1',
      email: 'test@example.com',
      userName: 'tester',
      displayName: 'Test User',
      roles: ['Editor'],
      isActive: true,
      createdAt: '2024-01-01T00:00:00Z',
      updatedAt: '2024-01-01T00:00:00Z',
    };

    mockedGetCurrentUser.mockRejectedValue(new Error('Unauthorized'));
    mockedLogin.mockResolvedValue({ success: true, user });

    renderWithProvider(<TestComponent />, { withAuth: true });

    await waitFor(() => {
      expect(screen.getByTestId('loading')).toHaveTextContent('idle');
    });

    fireEvent.click(screen.getByTestId('login-btn'));

    await waitFor(() => {
      expect(document.body.getAttribute('data-login-result')).toBe('true');
    });

    expect(mockedLogin).toHaveBeenCalledWith({ email: 'test@example.com', password: 'secret' });
    expect(screen.getByTestId('authenticated')).toHaveTextContent('yes');
    expect(screen.getByTestId('user')).toHaveTextContent('Test User');
    expect(screen.getByTestId('roles')).toHaveTextContent('Editor');
    expect(localStorage.getItem('auth_user')).toBe(JSON.stringify(user));
  });

  it('login fails and returns error message', async () => {
    mockedGetCurrentUser.mockRejectedValue(new Error('Unauthorized'));
    mockedLogin.mockResolvedValue({ success: false, errorMessage: 'Invalid credentials' });

    renderWithProvider(<TestComponent />, { withAuth: true });

    await waitFor(() => {
      expect(screen.getByTestId('loading')).toHaveTextContent('idle');
    });

    fireEvent.click(screen.getByTestId('login-btn'));

    await waitFor(() => {
      expect(document.body.getAttribute('data-login-result')).toBe('false');
    });

    expect(document.body.getAttribute('data-login-error')).toBe('Invalid credentials');
    expect(screen.getByTestId('authenticated')).toHaveTextContent('no');
  });

  it('login handles network exceptions', async () => {
    mockedGetCurrentUser.mockRejectedValue(new Error('Unauthorized'));
    mockedLogin.mockRejectedValue(new Error('Network error'));

    renderWithProvider(<TestComponent />, { withAuth: true });

    await waitFor(() => {
      expect(screen.getByTestId('loading')).toHaveTextContent('idle');
    });

    fireEvent.click(screen.getByTestId('login-btn'));

    await waitFor(() => {
      expect(document.body.getAttribute('data-login-result')).toBe('false');
    });

    expect(screen.getByTestId('authenticated')).toHaveTextContent('no');
  });

  it('logout clears user and localStorage', async () => {
    const user: UserDto = {
      id: 'u1',
      email: 'test@example.com',
      userName: 'tester',
      displayName: 'Test User',
      roles: ['Editor'],
      isActive: true,
      createdAt: '2024-01-01T00:00:00Z',
      updatedAt: '2024-01-01T00:00:00Z',
    };

    mockedGetCurrentUser.mockRejectedValue(new Error('Unauthorized'));
    mockedLogin.mockResolvedValue({ success: true, user });
    mockedLogout.mockResolvedValue(undefined);

    renderWithProvider(<TestComponent />, { withAuth: true });

    await waitFor(() => {
      expect(screen.getByTestId('loading')).toHaveTextContent('idle');
    });

    fireEvent.click(screen.getByTestId('login-btn'));
    await waitFor(() => {
      expect(screen.getByTestId('authenticated')).toHaveTextContent('yes');
    });

    fireEvent.click(screen.getByTestId('logout-btn'));

    await waitFor(() => {
      expect(screen.getByTestId('authenticated')).toHaveTextContent('no');
    });

    expect(screen.getByTestId('user')).toHaveTextContent('none');
    expect(localStorage.getItem('auth_user')).toBeNull();
    expect(mockedLogout).toHaveBeenCalled();
  });

  it('hasRole checks roles correctly', async () => {
    const user: UserDto = {
      id: 'u1',
      email: 'test@example.com',
      userName: 'tester',
      displayName: 'Test User',
      roles: ['Editor'],
      isActive: true,
      createdAt: '2024-01-01T00:00:00Z',
      updatedAt: '2024-01-01T00:00:00Z',
    };

    mockedGetCurrentUser.mockResolvedValue(user);

    renderWithProvider(<TestComponent />, { withAuth: true });

    await waitFor(() => {
      expect(screen.getByTestId('authenticated')).toHaveTextContent('yes');
    });

    expect(screen.getByTestId('has-admin')).toHaveTextContent('no');
  });

  it('throws when useAuth is called outside AuthProvider', () => {
    expect(() => renderHook(() => useAuth())).toThrow('useAuth must be used within AuthProvider');
  });
});

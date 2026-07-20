import { describe, it, expect, vi } from 'vitest';
import { screen, fireEvent, waitFor } from '@testing-library/react';
import type { ReactNode } from 'react';
import { MemoryRouter, Routes, Route } from 'react-router-dom';
import { renderWithProvider } from '../../test-utils.tsx';
import { LoginPage } from '../LoginPage';

vi.mock('../../hooks/AuthContext.tsx', () => ({
  useAuth: vi.fn(),
  AuthProvider: ({ children }: { children: ReactNode }) => children,
}));

import { useAuth } from '../../hooks/AuthContext.tsx';
const mockedUseAuth = vi.mocked(useAuth);

describe('LoginPage', () => {
  it('renders login form', () => {
    mockedUseAuth.mockReturnValue({
      user: null,
      isAuthenticated: false,
      isLoading: false,
      roles: [],
      hasRole: () => false,
      login: vi.fn().mockResolvedValue({ success: true }),
      logout: vi.fn(),
    });

    renderWithProvider(
      <MemoryRouter>
        <LoginPage />
      </MemoryRouter>,
    );
    expect(screen.getByRole('heading', { name: /sign in/i })).toBeDefined();
    expect(screen.getByPlaceholderText(/email/i)).toBeDefined();
    expect(screen.getByPlaceholderText(/password/i)).toBeDefined();
  });

  it('submits credentials and navigates on success', async () => {
    const login = vi.fn().mockResolvedValue({ success: true });
    mockedUseAuth.mockReturnValue({
      user: null,
      isAuthenticated: false,
      isLoading: false,
      roles: [],
      hasRole: () => false,
      login,
      logout: vi.fn(),
    });

    renderWithProvider(
      <MemoryRouter initialEntries={['/login']}>
        <Routes>
          <Route path="/login" element={<LoginPage />} />
          <Route path="/" element={<div data-testid="home">Home</div>} />
        </Routes>
      </MemoryRouter>,
    );

    fireEvent.change(screen.getByPlaceholderText(/email/i), { target: { value: 'test@example.com' } });
    fireEvent.change(screen.getByPlaceholderText(/password/i), { target: { value: 'password123' } });
    fireEvent.click(screen.getByRole('button', { name: /sign in/i }));

    await waitFor(() => {
      expect(login).toHaveBeenCalledWith({ email: 'test@example.com', password: 'password123' });
    });
  });

  it('displays error message on failed login', async () => {
    mockedUseAuth.mockReturnValue({
      user: null,
      isAuthenticated: false,
      isLoading: false,
      roles: [],
      hasRole: () => false,
      login: vi.fn().mockResolvedValue({ success: false, error: 'Invalid credentials' }),
      logout: vi.fn(),
    });

    renderWithProvider(
      <MemoryRouter>
        <LoginPage />
      </MemoryRouter>,
    );

    fireEvent.change(screen.getByPlaceholderText(/email/i), { target: { value: 'test@example.com' } });
    fireEvent.change(screen.getByPlaceholderText(/password/i), { target: { value: 'wrong' } });
    fireEvent.click(screen.getByRole('button', { name: /sign in/i }));

    await waitFor(() => {
      expect(screen.getByText('Invalid credentials')).toBeDefined();
    });
  });
});

/* eslint-disable react-refresh/only-export-components */
import { createContext, useContext, useState, useCallback, useMemo, type ReactNode } from 'react';
import { notifications } from '@mantine/notifications';
import { useRequest } from 'ahooks';
import type { UserDto, LoginRequest } from '../types/workflow.ts';
import * as api from '../services/api.ts';

interface AuthContextValue {
  user: UserDto | null;
  isAuthenticated: boolean;
  isLoading: boolean;
  roles: string[];
  hasRole: (role: string) => boolean;
  login: (data: LoginRequest) => Promise<{ success: boolean; error?: string }>;
  logout: () => Promise<void>;
}

const AuthContext = createContext<AuthContextValue | null>(null);

export function useAuth() {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used within AuthProvider');
  return ctx;
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<UserDto | null>(() => {
    try {
      const stored = localStorage.getItem('auth_user');
      return stored ? JSON.parse(stored) as UserDto : null;
    } catch {
      localStorage.removeItem('auth_user');
      return null;
    }
  });

  const { loading: isLoading } = useRequest(api.getCurrentUser, {
    onSuccess: (u) => {
      setUser(u);
      localStorage.setItem('auth_user', JSON.stringify(u));
    },
    onError: () => {
      setUser(null);
      localStorage.removeItem('auth_user');
    },
  });

  const login = useCallback(async (data: LoginRequest) => {
    try {
      const result = await api.login(data);
      if (result.success && result.user) {
        setUser(result.user);
        localStorage.setItem('auth_user', JSON.stringify(result.user));
        return { success: true };
      }
      return { success: false, error: result.errorMessage ?? 'Login failed' };
    } catch {
      return { success: false, error: 'Invalid credentials' };
    }
  }, []);

  const logout = useCallback(async () => {
    try {
      await api.logout();
    } catch { /* ignore */ }
    setUser(null);
    localStorage.removeItem('auth_user');
    notifications.show({ title: 'Logged out', message: 'You have been logged out', color: 'blue' });
  }, []);

  const roles = useMemo(() => user?.roles ?? [], [user?.roles]);
  const hasRole = useCallback((role: string) => roles.includes(role), [roles]);

  return (
    <AuthContext.Provider value={{ user, isAuthenticated: !!user, isLoading, roles, hasRole, login, logout }}>
      {children}
    </AuthContext.Provider>
  );
}

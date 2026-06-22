import { createContext, useContext, useState, useCallback, useEffect, type ReactNode } from 'react';
import { notifications } from '@mantine/notifications';
import type { UserDto, RegisterRequest, LoginRequest, RegisterResult } from '../types/workflow.ts';
import * as api from '../services/api.ts';

interface AuthContextValue {
  user: UserDto | null;
  token: string | null;
  isAuthenticated: boolean;
  isLoading: boolean;
  login: (data: LoginRequest) => Promise<{ success: boolean; error?: string }>;
  register: (data: RegisterRequest) => Promise<RegisterResult>;
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
    const stored = localStorage.getItem('auth_user');
    return stored ? JSON.parse(stored) : null;
  });
  const [token, setToken] = useState<string | null>(() => localStorage.getItem('auth_token'));
  const [isLoading, setIsLoading] = useState(true);

  useEffect(() => {
    if (!token) {
      setIsLoading(false);
      return;
    }
    api.getCurrentUser()
      .then((u) => {
        setUser(u);
        localStorage.setItem('auth_user', JSON.stringify(u));
      })
      .catch(() => {
        setToken(null);
        setUser(null);
        localStorage.removeItem('auth_token');
        localStorage.removeItem('auth_user');
      })
      .finally(() => setIsLoading(false));
  }, [token]);

  const login = useCallback(async (data: LoginRequest) => {
    try {
      const result = await api.login(data);
      if (result.success && result.token && result.user) {
        setToken(result.token);
        setUser(result.user);
        localStorage.setItem('auth_token', result.token);
        localStorage.setItem('auth_user', JSON.stringify(result.user));
        return { success: true };
      }
      return { success: false, error: result.errorMessage ?? 'Login failed' };
    } catch (err) {
      return { success: false, error: 'Invalid credentials' };
    }
  }, []);

  const register = useCallback(async (data: RegisterRequest) => {
    try {
      const result = await api.register(data);
      return result;
    } catch {
      return { success: false, errorMessage: 'Registration failed' };
    }
  }, []);

  const logout = useCallback(async () => {
    try {
      await api.logout();
    } catch { /* ignore */ }
    setToken(null);
    setUser(null);
    localStorage.removeItem('auth_token');
    localStorage.removeItem('auth_user');
    notifications.show({ title: 'Logged out', message: 'You have been logged out', color: 'blue' });
  }, []);

  return (
    <AuthContext.Provider value={{ user, token, isAuthenticated: !!token && !!user, isLoading, login, register, logout }}>
      {children}
    </AuthContext.Provider>
  );
}

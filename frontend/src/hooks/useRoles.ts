import { useAuth } from './AuthContext.tsx';

export function useRoles() {
  const { roles, hasRole } = useAuth();
  return { roles, hasRole };
}
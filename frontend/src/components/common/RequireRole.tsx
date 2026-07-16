import { useRoles } from '../../hooks/useRoles.ts';

interface RequireRoleProps {
  role: string;
  children: React.ReactNode;
  fallback?: React.ReactNode;
}

export function RequireRole({ role, children, fallback }: RequireRoleProps) {
  const { hasRole } = useRoles();

  if (!hasRole(role)) {
    return fallback ?? (
      <div style={{ padding: '2rem', textAlign: 'center', color: 'var(--mantine-color-dimmed)' }}>
        You do not have permission to access this page.
      </div>
    );
  }

  return <>{children}</>;
}
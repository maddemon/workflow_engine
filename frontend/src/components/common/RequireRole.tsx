import { useTranslation } from 'react-i18next';
import { useRoles } from '../../hooks/useRoles.ts';

interface RequireRoleProps {
  role: string;
  children: React.ReactNode;
  fallback?: React.ReactNode;
}

export function RequireRole({ role, children, fallback }: RequireRoleProps) {
  const { t } = useTranslation('common');
  const { hasRole } = useRoles();

  if (!hasRole(role)) {
    return fallback ?? (
      <div data-testid="permission-denied" style={{ padding: '2rem', textAlign: 'center', color: 'var(--mantine-color-dimmed)' }}>
        {t('noPermission')}
      </div>
    );
  }

  return <>{children}</>;
}
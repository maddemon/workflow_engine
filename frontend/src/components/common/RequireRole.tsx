import { useTranslation } from 'react-i18next';
import { useRoles } from '../../hooks/useRoles.ts';
import styles from './RequireRole.module.css';

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
      <div data-testid="permission-denied" className={styles.denied}>
        {t('noPermission')}
      </div>
    );
  }

  return <>{children}</>;
}
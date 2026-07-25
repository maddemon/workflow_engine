import type { ReactNode } from 'react';
import { HeaderToolbar } from './HeaderToolbar.tsx';
import { useLayout } from './LayoutContext.tsx';
import styles from './MainLayout.module.css';

export function MainLayout({ children }: { children: ReactNode }) {
  const { navbar, aside } = useLayout();

  return (
    <div className={styles.root}>
      <HeaderToolbar />
      <div className={styles.body}>
        {navbar && (
          <div className={styles.sidebar}>
            {navbar}
          </div>
        )}
        <main className={styles.main}>
          {children}
        </main>
        {aside && (
          <div className={styles.aside}>
            {aside}
          </div>
        )}
      </div>
    </div>
  );
}

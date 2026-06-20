import type { ReactNode } from 'react';
import { HeaderToolbar } from './HeaderToolbar.tsx';
import { useLayout } from './LayoutContext.tsx';

export function MainLayout({ children }: { children: ReactNode }) {
  const { navbar, aside } = useLayout();

  return (
    <div style={{ display: 'flex', flexDirection: 'column', height: '100vh', overflow: 'hidden' }}>
      <HeaderToolbar />
      <div style={{ flex: 1, display: 'flex', overflow: 'hidden' }}>
        {navbar && (
          <div style={{ width: 220, flexShrink: 0, borderRight: '1px solid var(--panel-border)', overflowY: 'auto', background: 'var(--bg-panel)' }}>
            {navbar}
          </div>
        )}
        <main style={{ flex: 1, position: 'relative', overflow: 'hidden' }}>
          {children}
        </main>
        {aside && (
          <div style={{ width: 300, flexShrink: 0, borderLeft: '1px solid var(--panel-border)', overflowY: 'auto', background: 'var(--bg-panel)' }}>
            {aside}
          </div>
        )}
      </div>
    </div>
  );
}

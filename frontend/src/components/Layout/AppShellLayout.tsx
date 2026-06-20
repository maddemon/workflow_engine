import type { ReactNode } from 'react';
import { AppShell } from '@mantine/core';

interface AppShellLayoutProps {
  navbar: ReactNode;
  aside: ReactNode;
  children: ReactNode;
}

export function AppShellLayout({ navbar, aside, children }: AppShellLayoutProps) {
  return (
    <AppShell
      header={{ height: 0 }}
      navbar={{ width: 220, breakpoint: 'sm' }}
      aside={{ width: 300, breakpoint: 'md' }}
      padding={0}
      h="100%"
    >
      <AppShell.Navbar p="xs" withBorder>
        <AppShell.Section grow style={{ overflowY: 'auto' }}>
          {navbar}
        </AppShell.Section>
      </AppShell.Navbar>
      <AppShell.Main style={{ position: 'relative' }}>{children}</AppShell.Main>
      <AppShell.Aside p="xs" withBorder>
        <AppShell.Section grow style={{ overflowY: 'auto' }}>
          {aside}
        </AppShell.Section>
      </AppShell.Aside>
    </AppShell>
  );
}

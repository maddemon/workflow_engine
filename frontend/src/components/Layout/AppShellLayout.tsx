import type { ReactNode } from 'react';
import { AppShell } from '@mantine/core';
import { HeaderToolbar } from './HeaderToolbar.tsx';

interface AppShellLayoutProps {
  /** 左侧节点面板 */
  navbar: ReactNode;
  /** 右侧参数/执行面板 */
  aside: ReactNode;
  /** 中间画布 */
  children: ReactNode;
}

/**
 * 三栏布局：Header + Navbar(左) + Main(画布) + Aside(右)。
 * 不使用 Mantine ScrollArea 包裹 Navbar，避免 ScrollArea 拦截鼠标事件导致 HTML5 拖拽失败。
 */
export function AppShellLayout({ navbar, aside, children }: AppShellLayoutProps) {
  return (
    <AppShell
      header={{ height: 52 }}
      navbar={{ width: 240, breakpoint: 'sm' }}
      aside={{ width: 340, breakpoint: 'md' }}
      padding={0}
      h="100vh"
    >
      <HeaderToolbar />
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

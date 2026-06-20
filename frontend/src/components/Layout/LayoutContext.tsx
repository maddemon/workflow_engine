import { createContext, useContext, type ReactNode } from 'react';

interface LayoutContextValue {
  navbar: ReactNode | null;
  aside: ReactNode | null;
}

const LayoutContext = createContext<LayoutContextValue>({ navbar: null, aside: null });

export function useLayout() {
  return useContext(LayoutContext);
}

export function LayoutProvider({ value, children }: { value: LayoutContextValue; children: ReactNode }) {
  return <LayoutContext.Provider value={value}>{children}</LayoutContext.Provider>;
}

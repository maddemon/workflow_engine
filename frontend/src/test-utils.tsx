import { type ReactElement } from 'react';
import { render, type RenderOptions } from '@testing-library/react';
import { MantineProvider } from '@mantine/core';

/**
 * Render a React element wrapped in MantineProvider.
 * Use for components that use Mantine UI primitives (Button, Modal, Stack, etc.).
 */
export function renderWithProvider(ui: ReactElement, options?: RenderOptions) {
  return render(<MantineProvider>{ui}</MantineProvider>, options);
}

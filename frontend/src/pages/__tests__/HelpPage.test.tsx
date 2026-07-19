import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen, waitFor } from '@testing-library/react';
import { renderWithProvider } from '../../test-utils.tsx';
import { HelpPage } from '../HelpPage';

vi.mock('../../services/api.ts', () => ({
  createApiKey: vi.fn(),
}));

import { createApiKey } from '../../services/api.ts';
const mockedCreateApiKey = vi.mocked(createApiKey);

describe('HelpPage', () => {
  beforeEach(() => {
    vi.resetModules();
    vi.clearAllMocks();
  });

  it('renders help sections', async () => {
    mockedCreateApiKey.mockResolvedValue({ id: 'key-1', name: 'test', key: 'secret-key', prefix: 'sec', expiresAt: null });

    renderWithProvider(<HelpPage />);
    expect(screen.getByRole('heading', { name: /help & mcp configuration/i })).toBeDefined();
    expect(screen.getByRole('heading', { name: /what is mcp/i })).toBeDefined();

    await waitFor(() => {
      expect(mockedCreateApiKey).toHaveBeenCalled();
    });
  });

  it('displays generated api key and mcp config', async () => {
    mockedCreateApiKey.mockResolvedValue({ id: 'key-1', name: 'test', key: 'secret-key', prefix: 'sec', expiresAt: null });

    renderWithProvider(<HelpPage />);
    await waitFor(() => {
      expect(screen.getByText('secret-key')).toBeDefined();
    });
    expect(screen.getAllByText(/flow-engine/i).length).toBeGreaterThanOrEqual(1);
  });
});

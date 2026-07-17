import { describe, it, expect, vi } from 'vitest';
import { screen } from '@testing-library/react';
import { renderWithProvider } from '../../test-utils.tsx';
import { AdminAuditPage } from '../AdminAuditPage';

// Mock the API module to prevent actual network calls
vi.mock('../../services/api.ts', () => ({
  queryAuditEvents: vi.fn().mockResolvedValue({ total: 0, offset: 0, limit: 20, events: [] }),
}));

describe('AdminAuditPage', () => {
  it('renders audit log title and search filters', () => {
    renderWithProvider(<AdminAuditPage />);
    expect(screen.getByRole('heading', { name: /audit/i })).toBeDefined();
    expect(screen.getByPlaceholderText(/event type/i)).toBeDefined();
    expect(screen.getByPlaceholderText(/resource type/i)).toBeDefined();
    expect(screen.getByRole('button', { name: /search/i })).toBeDefined();
    expect(screen.getByRole('button', { name: /reset/i })).toBeDefined();
  });
});

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
    expect(screen.getByText('Audit Log')).toBeDefined();
    expect(screen.getByPlaceholderText('Event Type')).toBeDefined();
    expect(screen.getByPlaceholderText('Resource Type')).toBeDefined();
    expect(screen.getByText('Search')).toBeDefined();
    expect(screen.getByText('Reset')).toBeDefined();
  });
});

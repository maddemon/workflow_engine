import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen, fireEvent, waitFor } from '@testing-library/react';
import { renderWithProvider } from '../../test-utils.tsx';
import { AdminAuditPage } from '../AdminAuditPage';
import * as api from '../../services/api.ts';
import type { AuditQueryResult } from '../../services/api.ts';

vi.mock('../../services/api.ts', () => ({
  queryAuditEvents: vi.fn(),
  getCurrentUser: vi.fn().mockRejectedValue(new Error('Unauthorized')),
}));

const mockedQueryAuditEvents = vi.mocked(api.queryAuditEvents);

function makeEvent(eventType: string, resourceType: string, resourceId: string, timestamp: string): Record<string, unknown> {
  return { eventType, resourceType, resourceId, timestamp, details: { extra: true } };
}

describe('AdminAuditPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockedQueryAuditEvents.mockResolvedValue({ total: 0, offset: 0, limit: 20, events: [] });
  });

  it('renders audit log title and search filters', () => {
    renderWithProvider(<AdminAuditPage />);
    expect(screen.getByRole('heading', { name: /audit/i })).toBeDefined();
    expect(screen.getByPlaceholderText(/event type/i)).toBeDefined();
    expect(screen.getByPlaceholderText(/resource type/i)).toBeDefined();
    expect(screen.getByRole('button', { name: /search/i })).toBeDefined();
    expect(screen.getByRole('button', { name: /reset/i })).toBeDefined();
  });

  it('displays audit events returned from the API', async () => {
    mockedQueryAuditEvents.mockResolvedValue({
      total: 1,
      offset: 0,
      limit: 20,
      events: [makeEvent('Login', 'User', 'u1', '2024-01-01T00:00:00Z')],
    });

    renderWithProvider(<AdminAuditPage />);

    await waitFor(() => {
      expect(screen.getByText('Login')).toBeDefined();
    });
    expect(screen.getByText('User')).toBeDefined();
    expect(screen.getByText('u1')).toBeDefined();
  });

  it('filters by event type when searching', async () => {
    mockedQueryAuditEvents.mockResolvedValue({
      total: 0,
      offset: 0,
      limit: 20,
      events: [],
    });

    renderWithProvider(<AdminAuditPage />);
    await waitFor(() => {
      expect(mockedQueryAuditEvents).toHaveBeenCalled();
    });

    fireEvent.change(screen.getByPlaceholderText(/event type/i), { target: { value: 'Login' } });
    fireEvent.click(screen.getByRole('button', { name: /search/i }));

    await waitFor(() => {
      expect(mockedQueryAuditEvents).toHaveBeenLastCalledWith(expect.objectContaining({ eventType: 'Login', offset: 0, limit: 20 }));
    });
  });

  it('resets filters and clears event type', async () => {
    mockedQueryAuditEvents.mockResolvedValue({
      total: 0,
      offset: 0,
      limit: 20,
      events: [],
    });

    renderWithProvider(<AdminAuditPage />);
    await waitFor(() => {
      expect(mockedQueryAuditEvents).toHaveBeenCalled();
    });

    const eventTypeInput = screen.getByPlaceholderText(/event type/i);
    fireEvent.change(eventTypeInput, { target: { value: 'Login' } });
    fireEvent.click(screen.getByRole('button', { name: /reset/i }));

    await waitFor(() => {
      expect(eventTypeInput).toHaveValue('');
    });

    await waitFor(() => {
      const lastCall = mockedQueryAuditEvents.mock.calls[mockedQueryAuditEvents.mock.calls.length - 1][0];
      expect(lastCall).toEqual(expect.objectContaining({ offset: 0, limit: 20 }));
      expect(lastCall).not.toHaveProperty('eventType');
    });
  });

  it('opens the audit detail drawer when a row is clicked', async () => {
    mockedQueryAuditEvents.mockResolvedValue({
      total: 1,
      offset: 0,
      limit: 20,
      events: [makeEvent('Login', 'User', 'u1', '2024-01-01T00:00:00Z')],
    });

    renderWithProvider(<AdminAuditPage />);

    const row = await screen.findByRole('button', { name: /login/i });
    fireEvent.click(row);

    await waitFor(() => {
      expect(screen.getByText(/audit event details/i)).toBeDefined();
    });
    expect(screen.getByText(/"eventType": "Login"/)).toBeDefined();
  });

  it('paginates through audit events', async () => {
    const events = Array.from({ length: 25 }, (_, i) =>
      makeEvent(`Event${i}`, 'Resource', `r${i}`, '2024-01-01T00:00:00Z'),
    );

    mockedQueryAuditEvents.mockImplementation(async (params) => {
      const offset = params.offset ?? 0;
      const limit = params.limit ?? 20;
      return {
        total: 25,
        offset,
        limit,
        events: events.slice(offset, offset + limit),
      } satisfies AuditQueryResult;
    });

    renderWithProvider(<AdminAuditPage />);

    await waitFor(() => {
      expect(screen.getByText('Event0')).toBeDefined();
    });

    // Click the page 2 button. Mantine Pagination renders buttons with aria-labels like "2".
    const pageTwo = screen.getByRole('button', { name: '2' });
    fireEvent.click(pageTwo);

    await waitFor(() => {
      expect(screen.getByText('Event20')).toBeDefined();
    });
    expect(screen.queryByText('Event0')).toBeNull();

    await waitFor(() => {
      expect(mockedQueryAuditEvents).toHaveBeenLastCalledWith(expect.objectContaining({ offset: 20, limit: 20 }));
    });
  });
});

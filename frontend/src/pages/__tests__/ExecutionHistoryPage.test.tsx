import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen, fireEvent, waitFor } from '@testing-library/react';
import { MemoryRouter, Routes, Route } from 'react-router-dom';
import { renderWithProvider } from '../../test-utils.tsx';
import { ExecutionHistoryPage } from '../ExecutionHistoryPage';
import type { ExecutionSummaryDto, ExecutionDto } from '../../types/workflow.ts';

vi.mock('../../services/api.ts', () => ({
  getWorkflowExecutions: vi.fn(),
  getExecution: vi.fn(),
}));

import { getWorkflowExecutions, getExecution } from '../../services/api.ts';
const mockedGetWorkflowExecutions = vi.mocked(getWorkflowExecutions);
const mockedGetExecution = vi.mocked(getExecution);

function makeSummary(id: string, status: ExecutionSummaryDto['status']): ExecutionSummaryDto {
  return {
    id,
    workflowDefinitionId: 'wf-1',
    status,
    startedAt: '2024-01-01T00:00:00Z',
    completedAt: status === 'Completed' ? '2024-01-01T00:01:00Z' : null,
  };
}

describe('ExecutionHistoryPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    Element.prototype.scrollIntoView = vi.fn();
  });

  it('renders empty state when no executions', async () => {
    mockedGetWorkflowExecutions.mockResolvedValue([]);

    renderWithProvider(
      <MemoryRouter initialEntries={['/workflows/wf-1/executions']}>
        <Routes>
          <Route path="/workflows/:id/executions" element={<ExecutionHistoryPage />} />
        </Routes>
      </MemoryRouter>,
    );

    await waitFor(() => {
      expect(mockedGetWorkflowExecutions).toHaveBeenCalledWith('wf-1');
    });
    expect(screen.getByText(/no executions found/i)).toBeDefined();
  });

  it('renders execution list and filters by status', async () => {
    mockedGetWorkflowExecutions.mockResolvedValue([
      makeSummary('ex-1', 'Completed'),
      makeSummary('ex-2', 'Failed'),
    ]);

    renderWithProvider(
      <MemoryRouter initialEntries={['/workflows/wf-1/executions']}>
        <Routes>
          <Route path="/workflows/:id/executions" element={<ExecutionHistoryPage />} />
        </Routes>
      </MemoryRouter>,
    );

    await waitFor(() => {
      const rows = screen.getAllByRole('row');
      expect(rows.length).toBeGreaterThanOrEqual(3);
    });

    fireEvent.click(screen.getByRole('combobox'));
    const completedOption = document.querySelector('[role="option"][value="Completed"]');
    expect(completedOption).not.toBeNull();
    fireEvent.click(completedOption!);

    await waitFor(() => {
      const rows = screen.getAllByRole('row');
      expect(rows.length).toBe(2);
    });
  });

  it('opens execution details modal', async () => {
    mockedGetWorkflowExecutions.mockResolvedValue([makeSummary('ex-1', 'Completed')]);
    const detail: ExecutionDto = {
      id: 'ex-1',
      workflowDefinitionId: 'wf-1',
      status: 'Completed',
      startedAt: '2024-01-01T00:00:00Z',
      completedAt: '2024-01-01T00:01:00Z',
      nodeRecords: [],
    };
    mockedGetExecution.mockResolvedValue(detail);

    renderWithProvider(
      <MemoryRouter initialEntries={['/workflows/wf-1/executions']}>
        <Routes>
          <Route path="/workflows/:id/executions" element={<ExecutionHistoryPage />} />
        </Routes>
      </MemoryRouter>,
    );

    await waitFor(() => {
      expect(screen.getAllByRole('row').length).toBeGreaterThanOrEqual(2);
    });
    const dataRow = screen.getAllByRole('row')[1];
    const viewButton = dataRow.querySelector('button');
    expect(viewButton).not.toBeNull();
    fireEvent.click(viewButton!);

    await waitFor(() => {
      expect(mockedGetExecution).toHaveBeenCalledWith('ex-1');
    });
  });
});

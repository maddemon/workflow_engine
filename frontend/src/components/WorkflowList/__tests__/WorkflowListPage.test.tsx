import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen, fireEvent, waitFor, within } from '@testing-library/react';
import { MemoryRouter, Routes, Route } from 'react-router-dom';
import { renderWithProvider } from '../../../test-utils.tsx';
import { WorkflowListPage } from '../WorkflowListPage.tsx';
import type { WorkflowSummary, ProjectDto, ImportResult, BatchImportResult, WorkflowExportResult } from '../../../types/workflow.ts';

vi.mock('../../../services/api.ts', () => ({
  getWorkflows: vi.fn(),
  getProjects: vi.fn(),
  exportWorkflow: vi.fn(),
  exportWorkflowsBatch: vi.fn(),
  importWorkflow: vi.fn(),
  importWorkflowsBatch: vi.fn(),
  deleteWorkflow: vi.fn(),
}));

vi.mock('../../../hooks/AuthContext.tsx', () => ({
  useAuth: vi.fn(),
}));

vi.mock('@mantine/notifications', () => ({
  notifications: { show: vi.fn() },
}));

import { getWorkflows, getProjects, exportWorkflow, exportWorkflowsBatch, importWorkflow, importWorkflowsBatch, deleteWorkflow } from '../../../services/api.ts';
import { useAuth } from '../../../hooks/AuthContext.tsx';

const mockedGetWorkflows = vi.mocked(getWorkflows);
const mockedGetProjects = vi.mocked(getProjects);
const mockedExportWorkflow = vi.mocked(exportWorkflow);
const mockedExportWorkflowsBatch = vi.mocked(exportWorkflowsBatch);
const mockedImportWorkflow = vi.mocked(importWorkflow);
const mockedImportWorkflowsBatch = vi.mocked(importWorkflowsBatch);
const mockedDeleteWorkflow = vi.mocked(deleteWorkflow);
const mockedUseAuth = vi.mocked(useAuth);

function makeWorkflow(id: string, name: string, projectId: string | null = null, opts: Partial<WorkflowSummary> = {}): WorkflowSummary {
  return {
    id,
    name,
    projectId,
    version: 1,
    isActive: true,
    createdAt: '2024-01-01',
    updatedAt: '2024-01-02',
    lastExecutionAt: null,
    triggerCount: 0,
    nextTriggerAt: null,
    ...opts,
  };
}

function makeProject(id: string, name: string): ProjectDto {
  return { id, name, description: null, createdBy: 'u', createdAt: '2024-01-01', updatedAt: null };
}

function renderPage() {
  return renderWithProvider(
    <MemoryRouter initialEntries={['/workflows']}>
      <Routes>
        <Route path="/workflows" element={<WorkflowListPage />} />
        <Route path="/workflow/new" element={<div data-testid="new-page">New Workflow</div>} />
        <Route path="/workflow/:id" element={<div data-testid="edit-page">Edit</div>} />
      </Routes>
    </MemoryRouter>,
  );
}

function getMenuButtonForWorkflow(name: string) {
  const row = screen.getByRole('row', { name: new RegExp(name, 'i') });
  const buttons = within(row).getAllByRole('button');
  const menuButton = buttons.find((b) => b.getAttribute('aria-haspopup') === 'menu');
  if (!menuButton) throw new Error(`Menu button not found for workflow ${name}`);
  return menuButton;
}

describe('WorkflowListPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockedUseAuth.mockReturnValue({ user: { id: 'u1', userName: 'tester', email: 't@t.com', displayName: 'Tester', roles: [], isActive: true, createdAt: '', updatedAt: '' }, isAuthenticated: true, isLoading: false, roles: [], hasRole: () => false, login: vi.fn(), logout: vi.fn() });
    mockedGetProjects.mockResolvedValue([makeProject('p1', 'Project A')]);
    mockedGetWorkflows.mockResolvedValue([]);
    mockedDeleteWorkflow.mockResolvedValue(undefined);
    window.confirm = vi.fn().mockReturnValue(true);
    globalThis.URL.createObjectURL = vi.fn().mockReturnValue('blob://x');
    globalThis.URL.revokeObjectURL = vi.fn();
    Element.prototype.scrollIntoView = vi.fn();
  });

  it('renders empty state and navigates to new workflow', async () => {
    renderPage();
    await waitFor(() => {
      expect(screen.getByText(/no workflows/i)).toBeDefined();
    });
    const newButtons = screen.getAllByRole('button', { name: /new workflow/i });
    fireEvent.click(newButtons[0]);
    await waitFor(() => {
      expect(screen.getByTestId('new-page')).toBeDefined();
    });
  });

  it('renders workflow rows with project badge', async () => {
    mockedGetWorkflows.mockResolvedValue([
      makeWorkflow('w1', 'Alpha', 'p1'),
      makeWorkflow('w2', 'Beta', null),
    ]);

    renderPage();
    await waitFor(() => {
      expect(screen.getByText('Alpha')).toBeDefined();
    });
    expect(screen.getAllByText('Project A').length).toBeGreaterThanOrEqual(1);
    expect(screen.getAllByText('Global').length).toBeGreaterThanOrEqual(1);
  });

  it('filters workflows by project', async () => {
    mockedGetWorkflows.mockResolvedValue([
      makeWorkflow('w1', 'Alpha', 'p1'),
      makeWorkflow('w2', 'Beta', null),
    ]);

    renderPage();
    await waitFor(() => {
      expect(screen.getByText('Alpha')).toBeDefined();
    });

    fireEvent.click(screen.getAllByRole('combobox')[0]);
    const option = document.querySelector('[role="option"][value="__none__"]');
    expect(option).not.toBeNull();
    fireEvent.click(option!);

    await waitFor(() => {
      expect(screen.queryByText('Alpha')).toBeNull();
    });
    expect(screen.getByText('Beta')).toBeDefined();
  });

  it('selects workflows and exports batch', async () => {
    mockedGetWorkflows.mockResolvedValue([
      makeWorkflow('w1', 'Alpha'),
      makeWorkflow('w2', 'Beta'),
    ]);
    const result: WorkflowExportResult = { name: 'Batch', version: 1, nodes: [], connections: [], exportedAt: '', exportedBy: '' };
    mockedExportWorkflowsBatch.mockResolvedValue([result]);

    renderPage();
    await waitFor(() => {
      expect(screen.getByText('Alpha')).toBeDefined();
    });

    const checkboxes = screen.getAllByRole('checkbox');
    fireEvent.click(checkboxes[1]);
    fireEvent.click(checkboxes[2]);

    fireEvent.click(screen.getByRole('button', { name: /export/i }));

    await waitFor(() => {
      expect(mockedExportWorkflowsBatch).toHaveBeenCalledWith(['w1', 'w2']);
    });
  });

  it('exports a single workflow from menu', async () => {
    mockedGetWorkflows.mockResolvedValue([makeWorkflow('w1', 'Alpha')]);
    const result: WorkflowExportResult = { name: 'Alpha', version: 1, nodes: [], connections: [], exportedAt: '', exportedBy: '' };
    mockedExportWorkflow.mockResolvedValue(result);

    renderPage();
    await waitFor(() => {
      expect(screen.getByText('Alpha')).toBeDefined();
    });

    const menuButton = getMenuButtonForWorkflow('Alpha');
    fireEvent.click(menuButton);
    const menu = await screen.findByRole('menu');
    const exportItem = within(menu).getByText(/export workflow/i);
    fireEvent.click(exportItem);

    await waitFor(() => {
      expect(mockedExportWorkflow).toHaveBeenCalledWith('w1');
    });
  });

  it('deletes a workflow after confirmation', async () => {
    mockedGetWorkflows.mockResolvedValue([makeWorkflow('w1', 'Alpha')]);

    renderPage();
    await waitFor(() => {
      expect(screen.getByText('Alpha')).toBeDefined();
    });

    const menuButton = getMenuButtonForWorkflow('Alpha');
    fireEvent.click(menuButton);
    const menu = await screen.findByRole('menu');
    const deleteItem = within(menu).getByText(/delete/i);
    fireEvent.click(deleteItem);

    await waitFor(() => {
      expect(mockedGetWorkflows).toHaveBeenCalledTimes(2);
    });
  });

  it('imports a single workflow json', async () => {
    mockedGetWorkflows.mockResolvedValue([]);
    const result: ImportResult = { success: true, workflowId: 'w1', workflowName: 'Imported', errors: [] };
    mockedImportWorkflow.mockResolvedValue(result);

    renderPage();
    await waitFor(() => {
      expect(screen.getByText(/no workflows/i)).toBeDefined();
    });

    fireEvent.click(screen.getAllByRole('button', { name: /import workflow/i })[0]);
    const dialog = await screen.findByRole('dialog', { name: /import workflows/i });

    const file = new File(['{}'], 'workflow.json', { type: 'application/json' });
    const input = dialog.querySelector('input[type="file"]');
    expect(input).not.toBeNull();
    fireEvent.change(input!, { target: { files: [file] } });

    fireEvent.click(within(dialog).getByRole('button', { name: /import workflow/i }));

    await waitFor(() => {
      expect(mockedImportWorkflow).toHaveBeenCalled();
    });
    await waitFor(() => {
      expect(screen.getByText(/Name:\s*Imported/)).toBeDefined();
    });
  });

  it('imports a batch workflow json', async () => {
    mockedGetWorkflows.mockResolvedValue([]);
    const result: BatchImportResult = { successCount: 1, failureCount: 0, results: [{ success: true, workflowId: 'w1', errors: [] }] };
    mockedImportWorkflowsBatch.mockResolvedValue(result);

    renderPage();
    await waitFor(() => {
      expect(screen.getByText(/no workflows/i)).toBeDefined();
    });

    fireEvent.click(screen.getAllByRole('button', { name: /import workflow/i })[0]);
    const dialog = await screen.findByRole('dialog', { name: /import workflows/i });

    const file = new File(['[]'], 'batch.json', { type: 'application/json' });
    const input = dialog.querySelector('input[type="file"]');
    expect(input).not.toBeNull();
    fireEvent.change(input!, { target: { files: [file] } });

    fireEvent.click(within(dialog).getByRole('button', { name: /import workflow/i }));

    await waitFor(() => {
      expect(mockedImportWorkflowsBatch).toHaveBeenCalled();
    });
  });
});

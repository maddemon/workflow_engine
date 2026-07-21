import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { screen, fireEvent, waitFor, within } from '@testing-library/react';
import { MemoryRouter, Routes, Route, useNavigate } from 'react-router-dom';
import { renderWithProvider } from '../../test-utils.tsx';
import { WorkflowEditorPage } from '../WorkflowEditorPage';
import { useWorkflowStore } from '../../stores/workflowStore.ts';
import type { NodeTypeDescriptor } from '../../types/workflow.ts';
import { useEffect, useState } from 'react';

vi.mock('../../hooks/useNodeTypes.ts', () => ({
  useNodeTypes: vi.fn(),
}));

vi.mock('../../hooks/useExecution.ts', () => ({
  useExecution: vi.fn(),
}));

vi.mock('../../hooks/useWorkflowVersionPolling.ts', () => ({
  useWorkflowVersionPolling: vi.fn(),
}));

vi.mock('../../services/api.ts', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../services/api.ts')>();
  return {
    ...actual,
    confirmWorkflow: vi.fn(),
    rejectDraft: vi.fn(),
    validateWorkflow: vi.fn(),
    getTriggers: vi.fn().mockResolvedValue([]),
  };
});

vi.mock('@mantine/notifications', () => ({
  notifications: { show: vi.fn() },
}));

import { useNodeTypes } from '../../hooks/useNodeTypes.ts';
import { useExecution } from '../../hooks/useExecution.ts';
import { useWorkflowVersionPolling } from '../../hooks/useWorkflowVersionPolling.ts';
import { confirmWorkflow, rejectDraft, validateWorkflow } from '../../services/api.ts';
import { notifications } from '@mantine/notifications';

const mockedUseNodeTypes = vi.mocked(useNodeTypes);
const mockedUseExecution = vi.mocked(useExecution);
const mockedUseWorkflowVersionPolling = vi.mocked(useWorkflowVersionPolling);
const mockedConfirmWorkflow = vi.mocked(confirmWorkflow);
const mockedRejectDraft = vi.mocked(rejectDraft);
const mockedValidateWorkflow = vi.mocked(validateWorkflow);
const mockedNotifications = vi.mocked(notifications);

const descriptor: NodeTypeDescriptor = {
  typeName: 'httpRequest',
  displayName: 'HTTP Request',
  category: 'Http',
  icon: '',
  executionMode: 'Sync',
  parameters: [],
  ports: [],
  defaultIsEntry: false,
};

// Drives a real route param change from inside the Router (useNavigate must be
// called within a Router context, so it lives in this inner component).
function NavigateOnProp({ navigateTo }: { navigateTo?: string }) {
  const navigate = useNavigate();
  useEffect(() => {
    if (navigateTo) navigate(navigateTo);
  }, [navigateTo, navigate]);
  return null;
}

function TestWrapper({ path = '/workflows/wf-1/edit', onLayoutChange, navigateTo }: { path?: string; onLayoutChange?: (navbar: React.ReactNode, aside: React.ReactNode) => void; navigateTo?: string }) {
  const [navbar, setNavbar] = useState<React.ReactNode>(null);
  const [aside, setAside] = useState<React.ReactNode>(null);

  return (
    <MemoryRouter initialEntries={[path]}>
      <NavigateOnProp navigateTo={navigateTo} />
      <Routes>
        <Route
          path="/workflows/:id/edit"
          element={
            <WorkflowEditorPage
              onLayoutChange={(n, a) => {
                setNavbar(n);
                setAside(a);
                onLayoutChange?.(n, a);
              }}
            />
          }
        />
      </Routes>
      {navbar && <div data-testid="navbar">{navbar}</div>}
      {aside && <div data-testid="aside">{aside}</div>}
    </MemoryRouter>
  );
}

function renderPage(path = '/workflows/wf-1/edit', onLayoutChange?: (navbar: React.ReactNode, aside: React.ReactNode) => void) {
  return renderWithProvider(<TestWrapper path={path} onLayoutChange={onLayoutChange} />);
}

// Harness keeps renderWithProvider's Mantine/i18n providers as the root node so
// that rerendering with a new path does NOT drop the providers.
function Harness({ path, navigateTo }: { path: string; navigateTo?: string }) {
  return <TestWrapper path={path} navigateTo={navigateTo} />;
}

describe('WorkflowEditorPage', () => {
  const originalConfirm = window.confirm;

  afterEach(() => {
    window.confirm = originalConfirm;
  });

  beforeEach(() => {
    vi.clearAllMocks();
    useWorkflowStore.getState().newWorkflow();
    useWorkflowStore.setState({
      nodeTypes: [descriptor],
      workflowId: 'wf-1',
      workflowName: 'Test Workflow',
      workflowVersion: 1,
    });

    mockedUseNodeTypes.mockReturnValue({ ready: true, nodeTypes: [descriptor] });
    mockedUseExecution.mockReturnValue({
      execution: null,
      status: 'idle',
      error: null,
      dryRunLoading: false,
      execute: vi.fn(),
      dryRun: vi.fn(),
      cancelExecution: vi.fn(),
      clearExecution: vi.fn(),
    });
    mockedUseWorkflowVersionPolling.mockReturnValue({ changed: false, newVersion: null, dismiss: vi.fn() });
  });

  it('loads_existingWorkflow_whenIdProvided', async () => {
    const loadWorkflow = vi.spyOn(useWorkflowStore.getState(), 'loadWorkflow').mockResolvedValue(undefined);
    renderPage('/workflows/wf-1/edit');

    await waitFor(() => {
      expect(loadWorkflow).toHaveBeenCalledWith('wf-1');
    });
  });

  it('creates_newWorkflow_whenIdIsNew', async () => {
    const newWorkflow = vi.spyOn(useWorkflowStore.getState(), 'newWorkflow');
    renderPage('/workflows/new/edit');

    await waitFor(() => {
      expect(newWorkflow).toHaveBeenCalled();
    });
  });

  it('waits_forNodeTypesReady_beforeLoading', async () => {
    const loadWorkflow = vi.spyOn(useWorkflowStore.getState(), 'loadWorkflow').mockResolvedValue(undefined);
    mockedUseNodeTypes.mockReturnValue({ ready: false, nodeTypes: [] });

    renderPage('/workflows/wf-1/edit');
    expect(loadWorkflow).not.toHaveBeenCalled();
  });

  it('renders_nodePanel_and_parameterPanel', async () => {
    renderPage('/workflows/wf-1/edit');
    expect(await screen.findByText('HTTP Request')).toBeInTheDocument();
    expect(screen.getByDisplayValue('Test Workflow')).toBeInTheDocument();
  });

  it('shows_executionPanel_whenExecutionExists', async () => {
    mockedUseExecution.mockReturnValue({
      execution: {
        id: 'ex-1',
        workflowDefinitionId: 'wf-1',
        status: 'Completed',
        startedAt: '2024-01-01T00:00:00Z',
        completedAt: '2024-01-01T00:01:00Z',
        nodeRecords: [],
      },
      status: 'completed',
      error: null,
      dryRunLoading: false,
      execute: vi.fn(),
      dryRun: vi.fn(),
      cancelExecution: vi.fn(),
      clearExecution: vi.fn(),
    });

    renderPage('/workflows/wf-1/edit');
    expect(await screen.findByText(/Execution Result/i)).toBeInTheDocument();
  });

  it('shows_errorPanel_whenExecutionErrorExists', async () => {
    mockedUseExecution.mockReturnValue({
      execution: null,
      status: 'failed',
      error: 'execution failed',
      dryRunLoading: false,
      execute: vi.fn(),
      dryRun: vi.fn(),
      cancelExecution: vi.fn(),
      clearExecution: vi.fn(),
    });

    renderPage('/workflows/wf-1/edit');
    expect(await screen.findByText(/Execution Error/i)).toBeInTheDocument();
  });

  it('switches_toReviewModePanel', async () => {
    useWorkflowStore.setState({
      reviewMode: true,
      structuredDiff: [{ op: 'add', nodeId: 'n1' }],
    });

    renderPage('/workflows/wf-1/edit');
    expect(await screen.findByText('Review Mode')).toBeInTheDocument();
    expect(screen.getByText('Confirm & Activate')).toBeInTheDocument();
    expect(screen.getByText('Reject')).toBeInTheDocument();
  });

  it('opens_validationModal_and_confirmsActivation', async () => {
    useWorkflowStore.setState({ reviewMode: true });
    mockedValidateWorkflow.mockResolvedValue({ valid: true, errors: [], canAutoFix: false });
    mockedConfirmWorkflow.mockResolvedValue({
      id: 'wf-1',
      projectId: null,
      name: 'Test',
      version: 1,
      createdBy: 'user',
      createdAt: '2024-01-01T00:00:00Z',
      updatedAt: '2024-01-01T00:00:00Z',
      isActive: true,
      styleSettings: { layoutDirection: 'horizontal' },
      nodes: [],
      connections: [],
    });

    renderPage('/workflows/wf-1/edit');
    fireEvent.click(await screen.findByText(/Confirm & Activate/i));

    const dialog = await screen.findByRole('dialog');
    expect(dialog).toBeInTheDocument();

    await waitFor(() => {
      expect(within(dialog).getByText(/all checks passed/i)).toBeInTheDocument();
    });

    const confirmButton = within(dialog).getByRole('button', { name: /confirm & activate/i });
    expect(confirmButton).not.toBeDisabled();
    fireEvent.click(confirmButton);

    await waitFor(() => {
      expect(mockedConfirmWorkflow).toHaveBeenCalledWith('wf-1');
    });
    expect(mockedNotifications.show).toHaveBeenCalledWith(expect.objectContaining({ color: 'green' }));
  });

  it('opens_rejectModal_and_submitsRejection', async () => {
    useWorkflowStore.setState({ reviewMode: true });
    mockedRejectDraft.mockResolvedValue({
      id: 'wf-1',
      projectId: null,
      name: 'Test',
      version: 1,
      createdBy: 'user',
      createdAt: '2024-01-01T00:00:00Z',
      updatedAt: '2024-01-01T00:00:00Z',
      isActive: false,
      styleSettings: { layoutDirection: 'horizontal' },
      nodes: [],
      connections: [],
    });

    renderPage('/workflows/wf-1/edit');
    fireEvent.click(await screen.findByText(/Reject/i));

    const textarea = await screen.findByPlaceholderText(/Describe what needs to be improved/i);
    fireEvent.change(textarea, { target: { value: 'needs work' } });

    const submitButton = screen.getByText(/Submit Rejection/i);
    expect(submitButton).not.toBeDisabled();

    fireEvent.click(submitButton);

    await waitFor(() => {
      expect(mockedRejectDraft).toHaveBeenCalledWith('wf-1', 'needs work');
    });
    expect(mockedNotifications.show).toHaveBeenCalledWith(expect.objectContaining({ color: 'orange' }));
  });

  it('shows_externalChangeAlert_andLoadsNewVersion', async () => {
    const dismiss = vi.fn();
    mockedUseWorkflowVersionPolling.mockReturnValue({ changed: true, newVersion: 2, dismiss });
    const loadWorkflow = vi.spyOn(useWorkflowStore.getState(), 'loadWorkflow').mockResolvedValue(undefined);
    window.confirm = vi.fn(() => true);

    renderPage('/workflows/wf-1/edit');
    const alert = await screen.findByText(/This workflow has been modified externally/i);
    expect(alert).toBeInTheDocument();

    fireEvent.click(screen.getByText(/Load new version/i));
    await waitFor(() => {
      expect(loadWorkflow).toHaveBeenCalledWith('wf-1');
    });
    expect(dismiss).toHaveBeenCalled();
  });

  it('externalChangeAlert_respectsUnsavedChangesConfirm', async () => {
    const dismiss = vi.fn();
    mockedUseWorkflowVersionPolling.mockReturnValue({ changed: true, newVersion: 2, dismiss });
    useWorkflowStore.setState({ isDirty: true });
    window.confirm = vi.fn(() => false);

    renderPage('/workflows/wf-1/edit');
    await screen.findByText(/This workflow has been modified externally/i);
    fireEvent.click(screen.getByText(/Load new version/i));

    expect(window.confirm).toHaveBeenCalled();
    expect(dismiss).not.toHaveBeenCalled();
  });

  it('calls_onLayoutChange_withNavbarAndAside', async () => {
    const onLayoutChange = vi.fn();
    renderPage('/workflows/wf-1/edit', onLayoutChange);

    await waitFor(() => {
      expect(onLayoutChange).toHaveBeenCalledWith(expect.anything(), expect.anything());
    });
  });

  // Regression test for P1: the load effect must NOT re-run (and must NOT clear
  // execution) when only the live execution status changes. The real useExecution
  // recreates `clearExecution` on every executionMeta update; if that identity is
  // in the load effect's dependency array the effect re-runs on every status message.
  it('does_not_reload_or_clear_execution_on_status_update', async () => {
    const loadWorkflow = vi.spyOn(useWorkflowStore.getState(), 'loadWorkflow').mockResolvedValue(undefined);

    // Initial render: no execution -> clearExecution identity "A"
    const clearExecutionA = vi.fn();
    mockedUseExecution.mockReturnValue({
      execution: null,
      status: 'idle',
      error: null,
      dryRunLoading: false,
      execute: vi.fn(),
      dryRun: vi.fn(),
      cancelExecution: vi.fn(),
      clearExecution: clearExecutionA,
    });

    const utils = renderWithProvider(<Harness path="/workflows/wf-1/edit" />);

    // Workflow loads once on mount; prior execution state cleared once.
    await waitFor(() => expect(loadWorkflow).toHaveBeenCalledTimes(1));
    expect(clearExecutionA).toHaveBeenCalled();

    // Simulate a live execution status update: a NEW clearExecution identity,
    // exactly as the real hook produces whenever executionMeta changes.
    const clearExecutionB = vi.fn();
    mockedUseExecution.mockReturnValue({
      execution: {
        id: 'ex-1',
        workflowDefinitionId: 'wf-1',
        status: 'Running',
        startedAt: '2024-01-01T00:00:00Z',
        completedAt: null,
        nodeRecords: [],
      },
      status: 'running',
      error: null,
      dryRunLoading: false,
      execute: vi.fn(),
      dryRun: vi.fn(),
      cancelExecution: vi.fn(),
      clearExecution: clearExecutionB,
    });

    utils.rerender(<Harness path="/workflows/wf-1/edit" />);

    // Execution status update must NOT reload the workflow nor clear execution again.
    expect(loadWorkflow).toHaveBeenCalledTimes(1);
    expect(clearExecutionB).not.toHaveBeenCalled();
  });

  // Switching to a different workflow id must clear the prior execution state.
  it('clears_execution_when_switching_workflow_id', async () => {
    const clearExecutionA = vi.fn();
    mockedUseExecution.mockReturnValue({
      execution: null,
      status: 'idle',
      error: null,
      dryRunLoading: false,
      execute: vi.fn(),
      dryRun: vi.fn(),
      cancelExecution: vi.fn(),
      clearExecution: clearExecutionA,
    });

    const utils = renderWithProvider(<Harness path="/workflows/wf-1/edit" />);
    await waitFor(() => expect(clearExecutionA).toHaveBeenCalled());

    // Switch to a different workflow id (real route param change, no remount)
    // -> clearExecution must run again with the new identity.
    const clearExecutionB = vi.fn();
    mockedUseExecution.mockReturnValue({
      execution: null,
      status: 'idle',
      error: null,
      dryRunLoading: false,
      execute: vi.fn(),
      dryRun: vi.fn(),
      cancelExecution: vi.fn(),
      clearExecution: clearExecutionB,
    });

    utils.rerender(<Harness path="/workflows/wf-1/edit" navigateTo="/workflows/wf-2/edit" />);

    await waitFor(() => expect(clearExecutionB).toHaveBeenCalledTimes(1));
  });
});

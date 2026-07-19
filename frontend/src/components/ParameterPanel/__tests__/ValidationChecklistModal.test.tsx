import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen, fireEvent, waitFor } from '@testing-library/react';
import { renderWithProvider } from '../../../test-utils.tsx';
import { ValidationChecklistModal } from '../ValidationChecklistModal.tsx';
import { useWorkflowStore } from '../../../stores/workflowStore.ts';

vi.mock('../../../services/api.ts', () => ({
  validateWorkflow: vi.fn(),
}));

import { validateWorkflow } from '../../../services/api.ts';
const mockedValidateWorkflow = vi.mocked(validateWorkflow);

describe('ValidationChecklistModal', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    useWorkflowStore.getState().newWorkflow();
  });

  it('does_not_fetch_when_closed', () => {
    useWorkflowStore.setState({ workflowId: 'wf-1' });
    renderWithProvider(<ValidationChecklistModal opened={false} onClose={vi.fn()} onProceed={vi.fn()} />);

    expect(mockedValidateWorkflow).not.toHaveBeenCalled();
  });

  it('does_not_fetch_without_workflowId', () => {
    renderWithProvider(<ValidationChecklistModal opened={true} onClose={vi.fn()} onProceed={vi.fn()} />);

    expect(mockedValidateWorkflow).not.toHaveBeenCalled();
  });

  it('shows_loading_state', () => {
    useWorkflowStore.setState({ workflowId: 'wf-1' });
    mockedValidateWorkflow.mockReturnValue(new Promise(() => {}));

    renderWithProvider(<ValidationChecklistModal opened={true} onClose={vi.fn()} onProceed={vi.fn()} />);
    expect(screen.getByText(/Validating\.{3}/i)).toBeInTheDocument();
  });

  it('shows_validation_result_valid', async () => {
    useWorkflowStore.setState({ workflowId: 'wf-1' });
    mockedValidateWorkflow.mockResolvedValue({ valid: true, errors: [], canAutoFix: false });

    renderWithProvider(<ValidationChecklistModal opened={true} onClose={vi.fn()} onProceed={vi.fn()} />);

    await waitFor(() => {
      expect(screen.getByText(/All checks passed/i)).toBeInTheDocument();
    });
  });

  it('shows_validation_result_invalid', async () => {
    useWorkflowStore.setState({ workflowId: 'wf-1' });
    mockedValidateWorkflow.mockResolvedValue({
      valid: false,
      errors: [
        { errorType: 'Required', message: 'Missing required field', nodeId: 'n1', suggestedFix: 'Fill the field' },
      ],
      canAutoFix: false,
    });

    renderWithProvider(<ValidationChecklistModal opened={true} onClose={vi.fn()} onProceed={vi.fn()} />);

    await waitFor(() => {
      expect(screen.getByText(/1 issue/i)).toBeInTheDocument();
    });
    expect(screen.getByText(/Missing required field/i)).toBeInTheDocument();
    expect(screen.getByText(/n1/i)).toBeInTheDocument();
    expect(screen.getByText(/Fill the field/i)).toBeInTheDocument();
  });

  it('shows_error_message', async () => {
    useWorkflowStore.setState({ workflowId: 'wf-1' });
    mockedValidateWorkflow.mockRejectedValue(new Error('validation failed'));

    renderWithProvider(<ValidationChecklistModal opened={true} onClose={vi.fn()} onProceed={vi.fn()} />);

    await waitFor(() => {
      expect(screen.getByText(/validation failed/i)).toBeInTheDocument();
    });
  });

  it('calls_onProceed_when_confirm_clicked', async () => {
    useWorkflowStore.setState({ workflowId: 'wf-1' });
    mockedValidateWorkflow.mockResolvedValue({ valid: true, errors: [], canAutoFix: false });
    const onProceed = vi.fn();

    renderWithProvider(<ValidationChecklistModal opened={true} onClose={vi.fn()} onProceed={onProceed} />);

    await waitFor(() => {
      expect(screen.getByText(/All checks passed/i)).toBeInTheDocument();
    });

    fireEvent.click(screen.getByText(/Confirm & Activate/i));
    expect(onProceed).toHaveBeenCalled();
  });

  it('calls_onClose_when_cancel_clicked', async () => {
    useWorkflowStore.setState({ workflowId: 'wf-1' });
    mockedValidateWorkflow.mockResolvedValue({ valid: true, errors: [], canAutoFix: false });
    const onClose = vi.fn();

    renderWithProvider(<ValidationChecklistModal opened={true} onClose={onClose} onProceed={vi.fn()} />);

    await waitFor(() => {
      expect(screen.getByText(/All checks passed/i)).toBeInTheDocument();
    });

    fireEvent.click(screen.getByText(/Cancel/i));
    expect(onClose).toHaveBeenCalled();
  });
});

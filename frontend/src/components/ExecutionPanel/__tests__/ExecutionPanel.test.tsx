import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen, fireEvent } from '@testing-library/react';
import { ExecutionPanel } from '../ExecutionPanel.tsx';
import { renderWithProvider } from '../../../test-utils.tsx';
import { useWorkflowStore } from '../../../stores/workflowStore.ts';
import type { ExecutionDto, NodeExecutionRecordDto } from '../../../types/workflow.ts';

vi.mock('../NodeOutputList.tsx', () => ({
  NodeOutputList: () => <div data-testid="node-output-list">NodeOutputList</div>,
}));

function makeExecution(overrides: Partial<ExecutionDto> = {}): ExecutionDto {
  return {
    id: 'exec-1',
    workflowDefinitionId: 'wf-1',
    status: 'Completed',
    startedAt: '2024-01-01T00:00:00Z',
    completedAt: '2024-01-01T00:00:05Z',
    nodeRecords: [],
    ...overrides,
  };
}

function makeRecord(status: NodeExecutionRecordDto['status'], nodeDefinitionId = 'n1'): NodeExecutionRecordDto {
  return {
    id: `${nodeDefinitionId}-0`,
    nodeDefinitionId,
    runIndex: 0,
    status,
    startedAt: '2024-01-01T00:00:00Z',
    completedAt: null,
    inputs: null,
    output: null,
    rawParameters: null,
    resolvedParameters: null,
  };
}

describe('ExecutionPanel', () => {
  beforeEach(() => {
    useWorkflowStore.getState().newWorkflow();
  });

  it('noExecution_noError_rendersNothing', () => {
    renderWithProvider(<ExecutionPanel execution={null} onClose={vi.fn()} />);
    expect(screen.queryByText('Execution Result')).not.toBeInTheDocument();
    expect(screen.queryByText('Execution Error')).not.toBeInTheDocument();
  });

  it('noExecution_withError_rendersErrorPanel', () => {
    renderWithProvider(<ExecutionPanel execution={null} onClose={vi.fn()} error="Something went wrong" />);
    expect(screen.getByText('Execution Error')).toBeInTheDocument();
    expect(screen.getByText('Something went wrong')).toBeInTheDocument();
  });

  it('errorPanel_closeClick_callsOnClose', () => {
    const onClose = vi.fn();
    renderWithProvider(<ExecutionPanel execution={null} onClose={onClose} error="err" />);
    fireEvent.click(screen.getByLabelText('Close'));
    expect(onClose).toHaveBeenCalled();
  });

  it('completedExecution_rendersResultAndStatus', () => {
    renderWithProvider(<ExecutionPanel execution={makeExecution()} onClose={vi.fn()} />);
    expect(screen.getByText('Execution Result')).toBeInTheDocument();
    expect(screen.getByText('Completed')).toBeInTheDocument();
    expect(screen.getByTestId('node-output-list')).toBeInTheDocument();
  });

  it('completedExecution_closeClick_callsOnClose', () => {
    const onClose = vi.fn();
    renderWithProvider(<ExecutionPanel execution={makeExecution()} onClose={onClose} />);
    fireEvent.click(screen.getByLabelText('Close'));
    expect(onClose).toHaveBeenCalled();
  });

  it('runningExecution_rendersCancelButton', () => {
    renderWithProvider(<ExecutionPanel execution={makeExecution({ status: 'Running' })} onClose={vi.fn()} onCancel={vi.fn()} />);
    expect(screen.getByText('Running')).toBeInTheDocument();
    expect(screen.getAllByText('Stop').length).toBeGreaterThan(0);
  });

  it('runningExecution_cancelClick_callsOnCancel', async () => {
    const onCancel = vi.fn().mockResolvedValue(undefined);
    renderWithProvider(<ExecutionPanel execution={makeExecution({ status: 'Running' })} onClose={vi.fn()} onCancel={onCancel} />);
    fireEvent.click(screen.getAllByText('Stop')[0]);
    await vi.waitFor(() => expect(onCancel).toHaveBeenCalled());
  });

  it('failedExecution_rendersErrorStatus', () => {
    renderWithProvider(<ExecutionPanel execution={makeExecution({ status: 'Failed' })} onClose={vi.fn()} error="exec error" />);
    expect(screen.getByText('Failed')).toBeInTheDocument();
    expect(screen.getByText('exec error')).toBeInTheDocument();
  });

  it('cancelledExecution_rendersCancelledStatus', () => {
    renderWithProvider(<ExecutionPanel execution={makeExecution({ status: 'Cancelled' })} onClose={vi.fn()} />);
    expect(screen.getByText('Cancelled')).toBeInTheDocument();
  });

  it('pendingExecution_rendersPendingStatus', () => {
    renderWithProvider(<ExecutionPanel execution={makeExecution({ status: 'Pending' })} onClose={vi.fn()} />);
    expect(screen.getByText('Pending')).toBeInTheDocument();
  });

  it('runningExecution_noRecords_rendersWaitingMessage', () => {
    renderWithProvider(<ExecutionPanel execution={makeExecution({ status: 'Running' })} onClose={vi.fn()} />);
    expect(screen.getByText('Waiting for execution to start...')).toBeInTheDocument();
  });

  it('runningExecution_withRecords_rendersStopExecutionButton', () => {
    useWorkflowStore.setState({
      nodeExecutionRecords: { n1: makeRecord('Running', 'n1') },
    });
    renderWithProvider(<ExecutionPanel execution={makeExecution({ status: 'Running' })} onClose={vi.fn()} onCancel={vi.fn()} />);
    expect(screen.getByText('Stop Execution')).toBeInTheDocument();
  });

  it('formatDuration_handlesMilliseconds', () => {
    renderWithProvider(
      <ExecutionPanel
        execution={makeExecution({ startedAt: '2024-01-01T00:00:00.000Z', completedAt: '2024-01-01T00:00:00.500Z' })}
        onClose={vi.fn()}
      />,
    );
    expect(screen.getByText('500ms')).toBeInTheDocument();
  });

  it('formatDuration_handlesSeconds', () => {
    renderWithProvider(
      <ExecutionPanel
        execution={makeExecution({ startedAt: '2024-01-01T00:00:00.000Z', completedAt: '2024-01-01T00:00:05.500Z' })}
        onClose={vi.fn()}
      />,
    );
    expect(screen.getByText('5.5s')).toBeInTheDocument();
  });

  it('formatDuration_handlesMinutes', () => {
    renderWithProvider(
      <ExecutionPanel
        execution={makeExecution({ startedAt: '2024-01-01T00:00:00.000Z', completedAt: '2024-01-01T00:01:05.000Z' })}
        onClose={vi.fn()}
      />,
    );
    expect(screen.getByText('1m 5s')).toBeInTheDocument();
  });
});

import { describe, it, expect } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { MantineProvider } from '@mantine/core';
import { AgentExecutionView } from '../AgentExecutionView.tsx';
import type { AgentExecutionData } from '../../../types/agent-execution.ts';

function makeData(overrides: Partial<AgentExecutionData> = {}): AgentExecutionData {
  return {
    agentInfo: {
      model: 'gpt-4',
      iterationCount: 1,
      status: 'Completed',
      startedAt: '2026-07-01T00:00:00.000Z',
      completedAt: '2026-07-01T00:00:05.000Z',
      errorMessage: null,
      tokenUsage: { promptTokens: 100, completionTokens: 50, totalTokens: 150 },
    },
    iterations: [
      {
        index: 0,
        llmChunks: [
          { content: 'Thinking...', role: 'assistant', timestamp: '2026-07-01T00:00:01.000Z' },
        ],
        toolCalls: [
          {
            id: 'tc-1',
            toolName: 'search',
            input: { q: 'test' },
            output: { result: 'ok' },
            status: 'Completed',
            duration: 120,
            error: null,
          },
        ],
        startedAt: '2026-07-01T00:00:01.000Z',
        completedAt: '2026-07-01T00:00:04.000Z',
      },
    ],
    subRecords: [],
    systemPrompt: 'You are a helpful assistant.',
    ...overrides,
  };
}

function renderWithProvider(ui: React.ReactElement) {
  return render(<MantineProvider>{ui}</MantineProvider>);
}

describe('AgentExecutionView', () => {
  it('renders agent model, iteration count and status badge', () => {
    const data = makeData();
    renderWithProvider(<AgentExecutionView data={data} />);

    expect(screen.getByText('Agent Execution')).toBeTruthy();
    expect(screen.getByText('gpt-4')).toBeTruthy();
    expect(screen.getByText(/1 iteration/)).toBeTruthy();
    expect(screen.getByText('Completed')).toBeTruthy();
  });

  it('shows error message when agent execution failed', () => {
    const data = makeData({
      agentInfo: {
        ...makeData().agentInfo,
        status: 'Failed',
        errorMessage: 'LLM call failed: timeout',
      },
    });
    renderWithProvider(<AgentExecutionView data={data} />);

    expect(screen.getByText('LLM call failed: timeout')).toBeTruthy();
    expect(screen.getByText('Failed')).toBeTruthy();
  });

  it('expands iteration and reveals tool calls on click', () => {
    const data = makeData();
    renderWithProvider(<AgentExecutionView data={data} />);

    const iterationHeader = screen.getByText('Iteration 1');
    fireEvent.click(iterationHeader);

    expect(screen.getByText('search')).toBeTruthy();
    expect(screen.getByText('Success')).toBeTruthy();
  });

  it('renders streaming indicator when streaming and running', () => {
    const data = makeData({
      agentInfo: { ...makeData().agentInfo, status: 'Running' },
    });
    renderWithProvider(<AgentExecutionView data={data} isStreaming={true} />);

    expect(screen.getByText('Streaming...')).toBeTruthy();
    expect(screen.getByText('Running')).toBeTruthy();
  });
});

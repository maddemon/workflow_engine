import { describe, it, expect } from 'vitest';
import { screen, fireEvent } from '@testing-library/react';
import { renderWithProvider } from '../../../test-utils.tsx';
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

function renderWithMantine(ui: React.ReactElement) {
  return renderWithProvider(ui);
}

describe('AgentExecutionView', () => {
  it('renders agent model, iteration count and status badge', () => {
    const data = makeData();
    renderWithMantine(<AgentExecutionView data={data} />);

    expect(screen.getByText('gpt-4')).toBeTruthy();
    expect(screen.getByText(/1 iteration/)).toBeTruthy();
    expect(screen.getByTestId('agent-status')).toBeTruthy();
  });

  it('shows error message when agent execution failed', async () => {
    const data = makeData({
      agentInfo: {
        ...makeData().agentInfo,
        status: 'Failed',
        errorMessage: 'LLM call failed: timeout',
      },
    });
    renderWithMantine(<AgentExecutionView data={data} />);

    // 使用异步查找，吸收 i18n 资源就绪前的渲染时序波动（避免偶发 flake）。
    expect(await screen.findByText('LLM call failed: timeout')).toBeTruthy();
    expect(screen.getByTestId('agent-status')).toBeTruthy();
  });

  it('expands iteration and reveals tool calls on click', () => {
    const data = makeData();
    renderWithMantine(<AgentExecutionView data={data} />);

    const iterationHeader = screen.getByTestId('iteration-0');
    fireEvent.click(iterationHeader);

    expect(screen.getByText('search')).toBeTruthy();
    expect(screen.getByTestId('tool-status')).toBeTruthy();
  });

  it('renders streaming indicator when streaming and running', () => {
    const data = makeData({
      agentInfo: { ...makeData().agentInfo, status: 'Running' },
    });
    renderWithMantine(<AgentExecutionView data={data} isStreaming={true} />);

    expect(screen.getByTestId('streaming-indicator')).toBeTruthy();
    expect(screen.getByTestId('agent-status')).toBeTruthy();
  });
});

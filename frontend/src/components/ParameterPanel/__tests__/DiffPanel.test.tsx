import { describe, it, expect, vi } from 'vitest';
import { screen, fireEvent } from '@testing-library/react';
import { renderWithProvider } from '../../../test-utils.tsx';
import { DiffPanel } from '../DiffPanel.tsx';
import type { StructuredDiff } from '../../../types/workflow.ts';

const diff: StructuredDiff[] = [
  { op: 'add', nodeId: 'n1' },
  { op: 'remove', nodeId: 'n2' },
  { op: 'modify', nodeId: 'n3', field: 'name', before: 'Old', after: 'New' },
  { op: 'connect', nodeId: 'n4', after: 'n1 -> n2' },
  { op: 'disconnect', nodeId: 'n5', before: 'n1 -> n2' },
  { op: 'unknown', nodeId: 'n6' },
];

describe('DiffPanel', () => {
  it('renders_diff_entries_and_count', () => {
    renderWithProvider(<DiffPanel diff={diff} highlightedNodeIds={[]} onNodeHighlight={vi.fn()} />);

    expect(screen.getByText(/Changes \(6\)/i)).toBeInTheDocument();
    expect(screen.getAllByText('n1').length).toBeGreaterThanOrEqual(1);
    expect(screen.getAllByText('n2').length).toBeGreaterThanOrEqual(1);
  });

  it('highlights_node_on_mouseEnter', () => {
    const onNodeHighlight = vi.fn();
    renderWithProvider(<DiffPanel diff={diff} highlightedNodeIds={[]} onNodeHighlight={onNodeHighlight} />);

    const nodeText = screen.getAllByText('n1')[0];
    const entry = nodeText.closest('[style*="cursor"]') ?? nodeText.parentElement;
    fireEvent.mouseEnter(entry!);
    expect(onNodeHighlight).toHaveBeenCalledWith(['n1']);

    fireEvent.mouseLeave(entry!);
    expect(onNodeHighlight).toHaveBeenCalledWith([]);
  });

  it('applies_highlight_background', () => {
    renderWithProvider(<DiffPanel diff={diff} highlightedNodeIds={['n1']} onNodeHighlight={vi.fn()} />);

    expect(screen.getAllByText('n1')[0]).toBeInTheDocument();
  });
});

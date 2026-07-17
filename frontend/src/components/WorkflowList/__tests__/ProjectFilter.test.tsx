import { describe, it, expect, vi } from 'vitest';
import { screen } from '@testing-library/react';
import { renderWithProvider } from '../../../test-utils.tsx';
import { ProjectFilter } from '../ProjectFilter';

vi.mock('../../../services/api.ts', () => ({
  getProjects: vi.fn().mockResolvedValue([
    { id: 'p1', name: 'Project Alpha' },
    { id: 'p2', name: 'Project Beta' },
  ]),
}));

describe('ProjectFilter', () => {
  it('renders filter placeholder', () => {
    renderWithProvider(<ProjectFilter value={null} onChange={vi.fn()} />);
    expect(screen.getByPlaceholderText(/filter by project/i)).toBeDefined();
  });
});

import { describe, it, expect, vi } from 'vitest';
import { screen } from '@testing-library/react';
import { renderWithProvider } from '../../test-utils.tsx';
import { AdminProjectsPage } from '../AdminProjectsPage';

vi.mock('../../services/api.ts', () => ({
  getProjects: vi.fn().mockResolvedValue([]),
  createProject: vi.fn(),
  updateProject: vi.fn(),
  deleteProject: vi.fn(),
}));

describe('AdminProjectsPage', () => {
  it('renders title and new project button', () => {
    renderWithProvider(<AdminProjectsPage />);
    expect(screen.getByRole('heading', { name: /project/i })).toBeDefined();
    expect(screen.getByRole('button', { name: /new project/i })).toBeDefined();
  });
});

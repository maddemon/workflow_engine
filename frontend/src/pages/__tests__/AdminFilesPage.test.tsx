import { describe, it, expect, vi } from 'vitest';
import { screen } from '@testing-library/react';
import { renderWithProvider } from '../../test-utils.tsx';
import { AdminFilesPage } from '../AdminFilesPage';

vi.mock('../../services/api.ts', () => ({
  getProjects: vi.fn().mockResolvedValue([]),
  listFiles: vi.fn().mockResolvedValue([]),
  uploadFile: vi.fn(),
  downloadFile: vi.fn(),
  deleteFile: vi.fn(),
  formatFileSize: vi.fn((b: number) => `${b} B`),
}));

describe('AdminFilesPage', () => {
  it('renders title and project selector', () => {
    renderWithProvider(<AdminFilesPage />);
    expect(screen.getByRole('heading', { name: /file/i })).toBeDefined();
    expect(screen.getByPlaceholderText(/select a project/i)).toBeDefined();
  });

  it('shows prompt to select a project when none selected', () => {
    renderWithProvider(<AdminFilesPage />);
    expect(screen.getByText(/select a project/i)).toBeDefined();
  });
});

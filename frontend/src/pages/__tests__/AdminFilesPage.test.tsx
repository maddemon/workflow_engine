import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen, fireEvent, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { renderWithProvider } from '../../test-utils.tsx';
import { AdminFilesPage } from '../AdminFilesPage';
import * as api from '../../services/api.ts';
import type { ProjectDto } from '../../types/workflow.ts';
import type { StoredFileDto } from '../../services/api.ts';

vi.mock('../../services/api.ts', () => ({
  getProjects: vi.fn().mockResolvedValue([]),
  listFiles: vi.fn().mockResolvedValue([]),
  uploadFile: vi.fn(),
  downloadFile: vi.fn(),
  deleteFile: vi.fn(),
  getCurrentUser: vi.fn().mockRejectedValue(new Error('Unauthorized')),
  formatFileSize: vi.fn((b: number) => `${b} B`),
}));

vi.mock('@mantine/core', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@mantine/core')>();
  return {
    ...actual,
    Select: ({ value, onChange, data }: { value?: string | null; onChange?: (v: string | null) => void; data: { value: string; label: string }[] }) => (
      <select value={value ?? ''} onChange={(e) => onChange?.(e.target.value || null)} data-testid="project-select">
        <option value="">Select a project</option>
        {data.map((item) => (
          <option key={item.value} value={item.value}>
            {item.label}
          </option>
        ))}
      </select>
    ),
  };
});

const mockedGetProjects = vi.mocked(api.getProjects);
const mockedListFiles = vi.mocked(api.listFiles);
const mockedDeleteFile = vi.mocked(api.deleteFile);

function makeProject(id: string, name: string): ProjectDto {
  return { id, name, description: null, createdBy: 'u1', createdAt: '2024-01-01T00:00:00Z', updatedAt: null };
}

function makeFile(id: string, fileName: string): StoredFileDto {
  return { id, fileName, contentType: 'text/plain', fileSize: 1024, createdAt: '2024-01-01T00:00:00Z' };
}

describe('AdminFilesPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockedGetProjects.mockResolvedValue([]);
    mockedListFiles.mockResolvedValue([]);
  });

  it('renders title and project selector', async () => {
    renderWithProvider(<AdminFilesPage />);
    expect(screen.getByRole('heading', { name: /file/i })).toBeDefined();
    await waitFor(() => {
      expect(screen.getByTestId('project-select')).toBeInTheDocument();
    });
  });

  it('shows prompt to select a project when none selected', async () => {
    renderWithProvider(<AdminFilesPage />);
    await waitFor(() => {
      expect(screen.getByText(/please select a project to view and manage its files/i)).toBeInTheDocument();
    });
  });

  async function selectProject(value: string, optionLabel: string) {
    await waitFor(() => {
      expect(screen.getByRole('option', { name: optionLabel })).toBeInTheDocument();
    });
    await userEvent.selectOptions(screen.getByTestId('project-select'), value);
  }

  it('lists files after selecting a project', async () => {
    mockedGetProjects.mockResolvedValue([makeProject('p1', 'Project A')]);
    mockedListFiles.mockResolvedValue([makeFile('f1', 'report.txt')]);

    renderWithProvider(<AdminFilesPage />);
    await selectProject('p1', 'Project A');

    await waitFor(() => {
      expect(screen.getByText('report.txt')).toBeInTheDocument();
    });
    expect(mockedListFiles).toHaveBeenCalledWith('p1');
  });

  it('shows upload button only when a project is selected', async () => {
    mockedGetProjects.mockResolvedValue([makeProject('p1', 'Project A')]);

    renderWithProvider(<AdminFilesPage />);
    expect(screen.queryByRole('button', { name: /upload file/i })).not.toBeInTheDocument();

    await selectProject('p1', 'Project A');

    await waitFor(() => {
      expect(screen.getByRole('button', { name: /upload file/i })).toBeInTheDocument();
    });
  });

  it('triggers file input when upload button is clicked', async () => {
    mockedGetProjects.mockResolvedValue([makeProject('p1', 'Project A')]);

    renderWithProvider(<AdminFilesPage />);
    await selectProject('p1', 'Project A');

    const uploadButton = await screen.findByRole('button', { name: /upload file/i });
    const fileInput = document.querySelector('input[type="file"]') as HTMLInputElement;
    const clickSpy = vi.spyOn(fileInput, 'click');

    fireEvent.click(uploadButton);

    await waitFor(() => {
      expect(clickSpy).toHaveBeenCalled();
    });
  });

  it('deletes a file after confirmation', async () => {
    mockedGetProjects.mockResolvedValue([makeProject('p1', 'Project A')]);
    mockedListFiles.mockResolvedValue([makeFile('f1', 'report.txt')]);
    mockedDeleteFile.mockResolvedValue(undefined);

    renderWithProvider(<AdminFilesPage />);
    await selectProject('p1', 'Project A');

    await waitFor(() => {
      expect(screen.getByText('report.txt')).toBeInTheDocument();
    });

    fireEvent.click(screen.getByRole('button', { name: /delete/i }));

    const dialog = await screen.findByRole('dialog');
    expect(dialog).toBeInTheDocument();

    fireEvent.click(within(dialog).getByRole('button', { name: /^delete$/i }));

    await waitFor(() => {
      expect(mockedDeleteFile).toHaveBeenCalledWith('f1');
    });
  });
});

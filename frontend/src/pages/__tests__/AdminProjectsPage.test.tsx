import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen, fireEvent, waitFor, within } from '@testing-library/react';
import { renderWithProvider } from '../../test-utils.tsx';
import { AdminProjectsPage } from '../AdminProjectsPage';
import * as api from '../../services/api.ts';
import type { ProjectDto } from '../../types/workflow.ts';

vi.mock('../../services/api.ts', () => ({
  getProjects: vi.fn(),
  createProject: vi.fn(),
  updateProject: vi.fn(),
  deleteProject: vi.fn(),
  getCurrentUser: vi.fn().mockRejectedValue(new Error('Unauthorized')),
}));

vi.mock('@mantine/notifications', () => ({
  notifications: { show: vi.fn() },
}));

const mockedGetProjects = vi.mocked(api.getProjects);
const mockedCreateProject = vi.mocked(api.createProject);
const mockedUpdateProject = vi.mocked(api.updateProject);
const mockedDeleteProject = vi.mocked(api.deleteProject);

function makeProject(id: string, name: string, description: string | null = null): ProjectDto {
  return {
    id,
    name,
    description,
    createdBy: 'u1',
    createdAt: '2024-01-01T00:00:00Z',
    updatedAt: null,
  };
}

describe('AdminProjectsPage', () => {
  let projects: ProjectDto[] = [];

  beforeEach(() => {
    vi.clearAllMocks();
    projects = [];
    mockedGetProjects.mockImplementation(() => Promise.resolve(projects));
    mockedCreateProject.mockImplementation((data) => {
      const created = makeProject(`p-${data.name.toLowerCase().replace(/\s+/g, '-')}`, data.name, data.description ?? null);
      projects.push(created);
      return Promise.resolve(created);
    });
    mockedUpdateProject.mockImplementation((id, data) => {
      const idx = projects.findIndex((p) => p.id === id);
      if (idx >= 0) {
        projects[idx] = { ...projects[idx], name: data.name, description: data.description ?? null };
        return Promise.resolve(projects[idx]);
      }
      return Promise.reject(new Error('Not found'));
    });
    mockedDeleteProject.mockImplementation((id) => {
      projects = projects.filter((p) => p.id !== id);
      return Promise.resolve(undefined);
    });
  });

  it('renders title and new project button', () => {
    renderWithProvider(<AdminProjectsPage />);
    expect(screen.getByRole('heading', { name: /project classification/i })).toBeDefined();
    expect(screen.getByRole('button', { name: /new project/i })).toBeDefined();
  });

  it('renders empty state when no projects', async () => {
    renderWithProvider(<AdminProjectsPage />);
    await waitFor(() => {
      expect(screen.getByText(/no projects yet/i)).toBeDefined();
    });
  });

  it('lists fetched projects', async () => {
    projects = [makeProject('p1', 'Project A', 'Desc A'), makeProject('p2', 'Project B')];
    renderWithProvider(<AdminProjectsPage />);

    await waitFor(() => {
      expect(screen.getByText('Project A')).toBeDefined();
    });
    expect(screen.getByText('Desc A')).toBeDefined();
    expect(screen.getByText('Project B')).toBeDefined();
  });

  it('creates a new project', async () => {
    renderWithProvider(<AdminProjectsPage />);
    await waitFor(() => {
      expect(screen.getByText(/no projects yet/i)).toBeDefined();
    });

    fireEvent.click(screen.getByRole('button', { name: /new project/i }));

    const modal = await screen.findByRole('dialog');
    expect(within(modal).getByText(/new project/i)).toBeDefined();

    const nameInput = within(modal).getByRole('textbox', { name: /name/i });
    fireEvent.change(nameInput, { target: { value: 'New Project' } });

    fireEvent.click(within(modal).getByRole('button', { name: /^create$/i }));

    await waitFor(() => {
      expect(mockedCreateProject).toHaveBeenCalledWith({ name: 'New Project', description: null });
    });

    await waitFor(() => {
      expect(screen.queryByRole('dialog')).toBeNull();
    });

    await waitFor(() => {
      expect(within(screen.getByRole('table')).getByText('New Project')).toBeDefined();
    });
  });

  it('validates project name is required', async () => {
    renderWithProvider(<AdminProjectsPage />);
    await waitFor(() => {
      expect(screen.getByText(/no projects yet/i)).toBeDefined();
    });

    fireEvent.click(screen.getByRole('button', { name: /new project/i }));

    const modal = await screen.findByRole('dialog');
    fireEvent.click(within(modal).getByRole('button', { name: /^create$/i }));

    await waitFor(() => {
      expect(screen.getByText(/project name is required/i)).toBeDefined();
    });
    expect(mockedCreateProject).not.toHaveBeenCalled();
  });

  it('edits an existing project', async () => {
    projects = [makeProject('p1', 'Old Name', 'Old desc')];
    renderWithProvider(<AdminProjectsPage />);

    await waitFor(() => {
      expect(screen.getByText('Old Name')).toBeDefined();
    });

    fireEvent.click(screen.getByRole('button', { name: /edit/i }));

    const modal = await screen.findByRole('dialog');
    expect(within(modal).getByText(/edit project/i)).toBeDefined();

    const nameInput = within(modal).getByRole('textbox', { name: /name/i });
    fireEvent.change(nameInput, { target: { value: 'Updated Name' } });

    const descriptionInput = within(modal).getByRole('textbox', { name: /description/i });
    fireEvent.change(descriptionInput, { target: { value: 'Updated desc' } });

    fireEvent.click(within(modal).getByRole('button', { name: /^update$/i }));

    await waitFor(() => {
      expect(mockedUpdateProject).toHaveBeenCalledWith('p1', { name: 'Updated Name', description: 'Updated desc' });
    });

    await waitFor(() => {
      expect(screen.queryByRole('dialog')).toBeNull();
    });

    await waitFor(() => {
      expect(screen.getByText('Updated Name')).toBeDefined();
    });
    expect(screen.getByText('Updated desc')).toBeDefined();
  });

  it('deletes a project after confirmation', async () => {
    projects = [makeProject('p1', 'Project A')];
    renderWithProvider(<AdminProjectsPage />);

    await waitFor(() => {
      expect(screen.getByText('Project A')).toBeDefined();
    });

    fireEvent.click(screen.getByRole('button', { name: /delete/i }));

    const modal = await screen.findByRole('dialog');
    expect(within(modal).getByText(/confirm delete/i)).toBeDefined();

    fireEvent.click(within(modal).getByRole('button', { name: /^delete$/i }));

    await waitFor(() => {
      expect(mockedDeleteProject).toHaveBeenCalledWith('p1');
    });

    await waitFor(() => {
      expect(screen.queryByRole('dialog')).toBeNull();
    });

    await waitFor(() => {
      expect(screen.queryByText('Project A')).toBeNull();
    });
  });
});

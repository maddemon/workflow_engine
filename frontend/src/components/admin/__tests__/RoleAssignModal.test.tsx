import { describe, it, expect, vi } from 'vitest';
import { screen } from '@testing-library/react';
import { renderWithProvider } from '../../../test-utils.tsx';
import { RoleAssignModal } from '../RoleAssignModal';

vi.mock('../../../services/api.ts', () => ({
  assignRole: vi.fn(),
  revokeRole: vi.fn(),
}));

describe('RoleAssignModal', () => {
  const defaultProps = {
    opened: true,
    onClose: vi.fn(),
    userId: 'user-1',
    userName: 'Test User',
    currentRoles: ['Editor'],
    onSaved: vi.fn(),
  };

  it('renders modal title with user name', () => {
    renderWithProvider(<RoleAssignModal {...defaultProps} />);
    expect(screen.getByText(/Manage Roles: Test User/)).toBeDefined();
  });

  it('shows all role options', () => {
    renderWithProvider(<RoleAssignModal {...defaultProps} />);
    expect(screen.getByText('Admin')).toBeDefined();
    expect(screen.getByText('Editor')).toBeDefined();
    expect(screen.getByText('Viewer')).toBeDefined();
  });

  it('checks the current roles', () => {
    renderWithProvider(<RoleAssignModal {...defaultProps} />);
    const editorCheckbox = screen.getByLabelText('Editor') as HTMLInputElement;
    expect(editorCheckbox.checked).toBe(true);
  });

  it('renders Save and Cancel buttons', () => {
    renderWithProvider(<RoleAssignModal {...defaultProps} />);
    expect(screen.getByText('Save')).toBeDefined();
    expect(screen.getByText('Cancel')).toBeDefined();
  });
});

import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen, fireEvent, waitFor } from '@testing-library/react';
import { renderWithProvider } from '../../../../test-utils.tsx';
import { CredentialField } from '../CredentialField.tsx';
import { useWorkflowStore } from '../../../../stores/workflowStore.ts';
import type { CredentialDto } from '../../../../types/workflow.ts';

vi.mock('../../../../services/api.ts', () => ({
  getCredentials: vi.fn(),
  createCredential: vi.fn(),
  getCredentialTypes: vi.fn().mockResolvedValue([]),
}));

vi.mock('@mantine/notifications', () => ({
  notifications: { show: vi.fn() },
}));

import { getCredentials, createCredential } from '../../../../services/api.ts';
const mockedGetCredentials = vi.mocked(getCredentials);
const mockedCreateCredential = vi.mocked(createCredential);

describe('CredentialField', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    useWorkflowStore.getState().newWorkflow();
    useWorkflowStore.setState({ selectedNodeId: 'n1' });
  });

  it('renders credentials and allows selection', async () => {
    const credentials: CredentialDto[] = [
      { id: 'c1', projectId: 'p1', name: 'Prod Key', type: 'apiKey', fields: {}, createdAt: '2024-01-01', updatedAt: '2024-01-01' },
    ];
    mockedGetCredentials.mockResolvedValue(credentials);
    const onChange = vi.fn();

    renderWithProvider(
      <CredentialField
        definition={{ name: 'credential', displayName: 'Credential', type: 'Credential', defaultValue: '', required: false, validationRules: [], displayRule: null, credentialType: 'apiKey', options: [] }}
        value=""
        onChange={onChange}
      />,
    );

    await waitFor(() => {
      expect(screen.getByText(/prod key \(apikey\)/i)).toBeDefined();
    });

    fireEvent.click(screen.getByRole('combobox'));
    const option = document.querySelector('[role="option"]');
    expect(option).not.toBeNull();
    fireEvent.click(option!);

    await waitFor(() => {
      expect(onChange).toHaveBeenCalledWith('Prod Key');
    });
  });

  it('opens create modal and creates a credential', async () => {
    mockedGetCredentials.mockResolvedValue([]);
    mockedCreateCredential.mockResolvedValue({ id: 'c2' } as unknown as CredentialDto);
    const onChange = vi.fn();

    renderWithProvider(
      <CredentialField
        definition={{ name: 'credential', displayName: 'Credential', type: 'Credential', defaultValue: '', required: false, validationRules: [], displayRule: null, credentialType: null, options: [] }}
        value=""
        onChange={onChange}
      />,
    );

    await waitFor(() => {
      expect(screen.getByRole('button', { name: /new/i })).toBeDefined();
    });

    fireEvent.click(screen.getByRole('button', { name: /new/i }));
    await waitFor(() => {
      expect(screen.getByLabelText(/name/i)).toBeDefined();
    });

    fireEvent.change(screen.getByLabelText(/name/i), { target: { value: 'Staging' } });
    fireEvent.click(screen.getByRole('button', { name: /^create$/i }));

    await waitFor(() => {
      expect(mockedCreateCredential).toHaveBeenCalledWith(expect.objectContaining({ name: 'Staging', type: 'apiKey' }));
    });
  });
});

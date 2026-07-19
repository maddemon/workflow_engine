import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen, fireEvent, waitFor } from '@testing-library/react';
import { renderWithProvider } from '../../../../test-utils.tsx';
import { FileField } from '../FileField.tsx';
import type { StoredFileDto } from '../../../../services/api.ts';

vi.mock('../../../../services/api.ts', () => ({
  uploadFile: vi.fn(),
  listFiles: vi.fn(),
}));

vi.mock('@mantine/notifications', () => ({
  notifications: { show: vi.fn() },
}));

import { uploadFile, listFiles } from '../../../../services/api.ts';
const mockedUploadFile = vi.mocked(uploadFile);
const mockedListFiles = vi.mocked(listFiles);

describe('FileField', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('shows warning when uploading without projectId', async () => {
    const onChange = vi.fn();
    renderWithProvider(
      <FileField
        definition={{ name: 'file', displayName: 'File', type: 'File', defaultValue: '', required: false, validationRules: [], displayRule: null, credentialType: null, options: [] }}
        value=""
        onChange={onChange}
      />,
    );

    const input = document.querySelector('input[type="file"]') as HTMLInputElement;
    const file = new File(['x'], 'test.txt');
    fireEvent.change(input, { target: { files: [file] } });

    await waitFor(() => {
      expect(mockedUploadFile).not.toHaveBeenCalled();
    });
  });

  it('uploads file and calls onChange with file id', async () => {
    const onChange = vi.fn();
    mockedUploadFile.mockResolvedValue({ id: 'f1', fileName: 'test.txt', fileSize: 1 } as unknown as StoredFileDto);

    renderWithProvider(
      <FileField
        definition={{ name: 'file', displayName: 'File', type: 'File', defaultValue: '', required: false, validationRules: [], displayRule: null, credentialType: null, options: [] }}
        value=""
        onChange={onChange}
        projectId="p1"
      />,
    );

    const input = document.querySelector('input[type="file"]') as HTMLInputElement;
    const file = new File(['x'], 'test.txt');
    fireEvent.change(input, { target: { files: [file] } });

    await waitFor(() => {
      expect(mockedUploadFile).toHaveBeenCalledWith(file, 'p1');
    });
    await waitFor(() => {
      expect(onChange).toHaveBeenCalledWith('f1');
    });
  });

  it('clears the selected value', () => {
    const onChange = vi.fn();
    renderWithProvider(
      <FileField
        definition={{ name: 'file', displayName: 'File', type: 'File', defaultValue: '', required: false, validationRules: [], displayRule: null, credentialType: null, options: [] }}
        value="some-file"
        onChange={onChange}
        projectId="p1"
      />,
    );

    fireEvent.click(screen.getByRole('button', { name: /clear/i }));
    expect(onChange).toHaveBeenCalledWith('');
  });

  it('fetches file name when value is a uuid', async () => {
    const fileId = '550e8400-e29b-41d4-a716-446655440000';
    mockedListFiles.mockResolvedValue([{ id: fileId, fileName: 'report.pdf', fileSize: 1024 } as unknown as StoredFileDto]);
    const onChange = vi.fn();

    renderWithProvider(
      <FileField
        definition={{ name: 'file', displayName: 'File', type: 'File', defaultValue: '', required: false, validationRules: [], displayRule: null, credentialType: null, options: [] }}
        value={fileId}
        onChange={onChange}
        projectId="p1"
      />,
    );

    await waitFor(() => {
      expect(mockedListFiles).toHaveBeenCalledWith('p1');
    });
    await waitFor(() => {
      expect(screen.getByText('report.pdf')).toBeDefined();
    });
  });
});

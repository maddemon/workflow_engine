import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { FileField } from '../FileField.tsx';
import { renderWithProvider } from '../../../../test-utils.tsx';
import type { ParameterDefinition } from '../../../../types/workflow.ts';
import { uploadFile } from '../../../../services/api.ts';

vi.mock('../../../../services/api.ts', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../../../services/api.ts')>();
  return {
    ...actual,
    uploadFile: vi.fn(),
    listFiles: vi.fn().mockResolvedValue([]),
  };
});

const mockedUploadFile = vi.mocked(uploadFile);

// FileField only reads name/displayName/required/description from the definition,
// so a minimal object cast to the full type keeps the test focused.
const definition = {
  name: 'attachment',
  displayName: 'Attachment',
  type: 'file',
  defaultValue: '',
  required: false,
  validationRules: [],
  displayRule: null,
  credentialType: null,
  options: [],
} as unknown as ParameterDefinition;

function uploadFileHelper(container: HTMLElement, fileName = 'test.txt') {
  const input = container.querySelector('input[type="file"]') as HTMLInputElement;
  const file = new File(['hello'], fileName, { type: 'text/plain' });
  Object.defineProperty(input, 'files', { value: [file], configurable: true });
  fireEvent.change(input, { target: { files: [file] } });
  return { input, file };
}

describe('FileField', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('uploads file and shows loading state during the request', async () => {
    const onChange = vi.fn();
    let resolveUpload!: (value: unknown) => void;
    mockedUploadFile.mockImplementation(
      () => new Promise((resolve) => { resolveUpload = resolve; }) as unknown as ReturnType<typeof uploadFile>,
    );

    const { container } = renderWithProvider(
      <FileField definition={definition} value="" onChange={onChange} projectId="p-1" />,
    );

    const uploadButton = () => screen.getByRole('button', { name: /upload file/i });
    expect(uploadButton()).not.toBeDisabled();

    const { file } = uploadFileHelper(container);

    // While the upload is in flight the action button reflects loading (disabled).
    expect(uploadButton()).toBeDisabled();

    resolveUpload({ id: 'file-1', fileName: 'test.txt', fileSize: 5 });

    await waitFor(() => expect(mockedUploadFile).toHaveBeenCalledWith(file, 'p-1'));
    await waitFor(() => expect(onChange).toHaveBeenCalledWith('file-1'));
    // Once finished the button is interactive again.
    await waitFor(() => expect(uploadButton()).not.toBeDisabled());
  });

  it('shows error notification and does not call onChange when upload fails', async () => {
    const onChange = vi.fn();
    mockedUploadFile.mockRejectedValue(new Error('boom'));

    const { container } = renderWithProvider(
      <FileField definition={definition} value="" onChange={onChange} projectId="p-1" />,
    );

    uploadFileHelper(container);

    await waitFor(() => expect(mockedUploadFile).toHaveBeenCalled());
    expect(onChange).not.toHaveBeenCalled();
  });
});

import { useState, useRef, useMemo } from 'react';
import { Group, Text, ActionIcon, Tooltip } from '@mantine/core';
import { notifications } from '@mantine/notifications';
import { Upload, X } from 'lucide-react';
import { useRequest } from 'ahooks';
import { InfoTooltip } from './InfoTooltip.tsx';
import { uploadFile, listFiles } from '../../../services/api.ts';
import type { ParameterDefinition } from '../../../types/workflow.ts';

interface FileFieldProps {
  definition: ParameterDefinition;
  value: unknown;
  onChange: (value: string) => void;
  error?: string;
  /** When provided, file field can upload to the given project. */
  projectId?: string | null;
}

export function FileField({ definition, value, onChange, error, projectId }: FileFieldProps) {
  const [uploading, setUploading] = useState(false);
  const inputRef = useRef<HTMLInputElement>(null);

  const handleFileSelect = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (!file) return;
    if (!projectId) {
      notifications.show({ title: 'Error', message: 'Assign a project to enable file upload', color: 'yellow' });
      return;
    }
    setUploading(true);
    try {
      const result = await uploadFile(file, projectId);
      onChange(result.id); // save the file ID
      notifications.show({ title: 'Uploaded', message: `"${file.name}" uploaded.`, color: 'green' });
    } catch (err) {
      notifications.show({
        title: 'Upload failed',
        message: err instanceof Error ? err.message : 'Upload failed',
        color: 'red',
      });
    } finally {
      setUploading(false);
      if (inputRef.current) inputRef.current.value = '';
    }
  };

  const strVal = typeof value === 'string' ? value : '';
  const isUuid = (v: string) => /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(v);

  const { data: projectFiles = [] } = useRequest(
    () => listFiles(projectId!),
    {
      ready: !!projectId && isUuid(strVal),
      refreshDeps: [projectId, strVal],
    },
  );

  const fileName = useMemo(() => {
    if (!isUuid(strVal)) return strVal;
    const file = projectFiles.find(f => f.id === strVal);
    return file ? file.fileName : `File: ${strVal.slice(0, 8)}…`;
  }, [strVal, projectFiles]);

  return (
    <div>
      <Group gap={4} mb={4}>
        <Text size="xs" fw={400}>
          {definition.displayName}
          {definition.required && <span style={{ color: 'var(--mantine-color-error)' }}> *</span>}
        </Text>
        {definition.description && <InfoTooltip label={definition.description} />}
      </Group>
      <Group gap="xs">
        <input
          ref={inputRef}
          type="file"
          style={{ display: 'none' }}
          onChange={handleFileSelect}
          disabled={!projectId || uploading}
        />
        <Tooltip label={!projectId ? 'Assign a project to enable file upload' : 'Upload file'}>
          <ActionIcon
            variant="outline"
            size="sm"
            aria-label={!projectId ? 'Assign a project to enable file upload' : 'Upload file'}
            onClick={() => inputRef.current?.click()}
            disabled={!projectId || uploading}
            loading={uploading}
          >
            <Upload size={14} />
          </ActionIcon>
        </Tooltip>
        {strVal && (
          <>
            <Text size="sm">{fileName}</Text>
            <Tooltip label="Clear">
              <ActionIcon variant="subtle" color="red" size="xs" aria-label="Clear" onClick={() => onChange('')}>
                <X size={12} />
              </ActionIcon>
            </Tooltip>
          </>
        )}
      </Group>
      {error && (
        <Text size="xs" c="red" mt={4}>{error}</Text>
      )}
    </div>
  );
}

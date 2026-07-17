import { useState, useRef, useMemo } from 'react';
import { Group, Text, ActionIcon, Tooltip } from '@mantine/core';
import { notifications } from '@mantine/notifications';
import { Upload, X } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { useRequest } from 'ahooks';
import { InfoTooltip } from './InfoTooltip.tsx';
import { useParameterName } from '../useParameterName.ts';
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
  const { t } = useTranslation('parameterPanel');
  const paramName = useParameterName();
  const label = paramName(definition.name, definition.displayName);
  const [uploading, setUploading] = useState(false);
  const inputRef = useRef<HTMLInputElement>(null);

  const handleFileSelect = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (!file) return;
    if (!projectId) {
      notifications.show({ title: t('fields.file.uploadError'), message: t('fields.file.uploadError'), color: 'yellow' });
      return;
    }
    setUploading(true);
    try {
      const result = await uploadFile(file, projectId);
      onChange(result.id); // save the file ID
      notifications.show({ title: t('fields.file.uploaded'), message: t('fields.file.uploadedMessage', { name: file.name }), color: 'green' });
    } catch (err) {
      notifications.show({
        title: t('fields.file.uploadFailed'),
        message: err instanceof Error ? err.message : t('fields.file.uploadFailed'),
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
          {label}
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
        <Tooltip label={!projectId ? t('fields.file.assignProjectTooltip') : t('fields.file.uploadFile')}>
          <ActionIcon
            variant="outline"
            size="sm"
            aria-label={!projectId ? t('fields.file.assignProjectTooltip') : t('fields.file.uploadFile')}
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
            <Tooltip label={t('fields.file.clear')}>
              <ActionIcon variant="subtle" color="red" size="xs" aria-label={t('fields.file.clear')} onClick={() => onChange('')}>
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

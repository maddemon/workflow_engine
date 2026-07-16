import { useMemo, useState } from 'react';
import { notifications } from '@mantine/notifications';
import { Select, Group, Text, Button, Modal, Stack, TextInput, PasswordInput } from '@mantine/core';
import { useTranslation } from 'react-i18next';
import { Plus } from 'lucide-react';
import { useRequest } from 'ahooks';
import { InfoTooltip } from './InfoTooltip.tsx';
import { getCredentials, createCredential, getCredentialTypes } from '../../../services/api.ts';
import { useWorkflowStore } from '../../../stores/workflowStore.ts';
import type { ParameterDefinition, CredentialTypeDefinition } from '../../../types/workflow.ts';

interface CredentialFieldProps {
  definition: ParameterDefinition;
  value: unknown;
  onChange: (value: string) => void;
  error?: string;
}

export function CredentialField({ definition, value, onChange, error }: CredentialFieldProps) {
  const { t } = useTranslation('parameterPanel');
  const credentialRevision = useWorkflowStore((s) => s.credentialRevision);
  const bumpCredentialRevision = useWorkflowStore((s) => s.bumpCredentialRevision);

  const [createOpen, setCreateOpen] = useState(false);
  const [formType, setFormType] = useState('apiKey');
  const [formValues, setFormValues] = useState<Record<string, string>>({});
  const [formName, setFormName] = useState('');

  const { data: allCredentials = [], loading } = useRequest(getCredentials, {
    refreshDeps: [credentialRevision, definition.credentialType],
  });

  const credentials = useMemo(
    () => definition.credentialType
      ? allCredentials.filter((c) => c.type === definition.credentialType)
      : allCredentials,
    [allCredentials, definition.credentialType],
  );

  const { data: types = [] } = useRequest(getCredentialTypes);

  const typeOptions = types.length > 0
    ? types.map((t: CredentialTypeDefinition) => ({ label: t.displayName, value: t.name }))
    : [
        { label: 'API Key', value: 'apiKey' },
        { label: 'OAuth2', value: 'oauth2' },
        { label: 'Basic Auth', value: 'basicAuth' },
        { label: 'Database', value: 'database' },
      ];

  // 选中的凭据类型定义，用于按 schema 生成表单字段
  const selectedType = useMemo(
    () => types.find((t: CredentialTypeDefinition) => t.name === formType) ?? null,
    [types, formType],
  );

  const handleTypeChange = (next: string | null) => {
    const type = next ?? 'apiKey';
    setFormType(type);
    const def = types.find((t: CredentialTypeDefinition) => t.name === type);
    const initial: Record<string, string> = {};
    def?.fields.forEach((f) => { initial[f.name] = ''; });
    setFormValues(initial);
  };

  const handleOpenCreate = () => {
    const initial: Record<string, string> = {};
    selectedType?.fields.forEach((f) => { initial[f.name] = ''; });
    setFormValues(initial);
    setCreateOpen(true);
  };

  const handleCreate = async () => {
    if (!formName.trim()) {
      notifications.show({
        title: t('fields.file.uploadError'),
        message: t('fields.credential.nameRequired'),
        color: 'red',
      });
      return;
    }
    try {
      await createCredential({ name: formName, type: formType, fields: { ...formValues } });
      setCreateOpen(false);
      setFormName('');
      setFormValues({});
      bumpCredentialRevision();
    } catch (err) {
      notifications.show({
        title: t('fields.file.uploadError'),
        message: err instanceof Error ? err.message : t('fields.credential.createFailed'),
        color: 'red',
      });
    }
  };

  return (
    <div>
      <Group gap={4} mb={4} justify="space-between" wrap="nowrap">
        <Group gap={4} wrap="nowrap">
          <Text size="xs" fw={400}>
            {definition.displayName}
            {definition.required && <span style={{ color: 'var(--mantine-color-error)' }}> *</span>}
          </Text>
          {definition.description && <InfoTooltip label={definition.description} />}
        </Group>
        <Button
          size="compact-xs"
          variant="subtle"
          leftSection={<Plus size={12} />}
          onClick={handleOpenCreate}
          disabled={loading}
        >
          {t('fields.credential.new')}
        </Button>
      </Group>
      <Select
        error={error}
        value={String(value ?? '')}
        onChange={(v) => onChange(v ?? '')}
        placeholder={t('fields.credential.selectPlaceholder')}
        data={credentials.map((c) => ({ label: `${c.name} (${c.type})`, value: c.name }))}
        searchable
        disabled={loading}
      />
      <Modal opened={createOpen} onClose={() => setCreateOpen(false)} title={t('fields.credential.newCredential')} size="lg">
        <Stack gap="sm">
          <TextInput label={t('fields.credential.name')} value={formName} onChange={(e) => setFormName(e.target.value)} size="sm" />
          <Select
            label={t('fields.credential.type')}
            value={formType}
            onChange={handleTypeChange}
            data={typeOptions}
            size="sm"
          />
          <Stack gap="xs">
            {selectedType?.fields.map((field) => (
              <Stack gap={2} key={field.name}>
                <Group gap={4} wrap="nowrap">
                  <Text size="sm" fw={500}>
                    {field.displayName}
                    {field.required && <span style={{ color: 'var(--mantine-color-error)' }}> *</span>}
                  </Text>
                  {field.hint && <InfoTooltip label={field.hint} />}
                </Group>
                {field.sensitive ? (
                  <PasswordInput
                    placeholder={field.name}
                    value={formValues[field.name] ?? ''}
                    onChange={(e) => setFormValues((prev) => ({ ...prev, [field.name]: e.target.value }))}
                    size="sm"
                  />
                ) : (
                  <TextInput
                    placeholder={field.name}
                    value={formValues[field.name] ?? ''}
                    onChange={(e) => setFormValues((prev) => ({ ...prev, [field.name]: e.target.value }))}
                    size="sm"
                  />
                )}
              </Stack>
            ))}
            {selectedType && selectedType.fields.length === 0 && (
              <Text size="xs" c="dimmed">{t('fields.credential.noFields')}</Text>
            )}
          </Stack>
          <Group justify="flex-end">
            <Button variant="default" onClick={() => setCreateOpen(false)}>{t('fields.credential.cancel')}</Button>
            <Button onClick={handleCreate}>{t('fields.credential.create')}</Button>
          </Group>
        </Stack>
      </Modal>
    </div>
  );
}

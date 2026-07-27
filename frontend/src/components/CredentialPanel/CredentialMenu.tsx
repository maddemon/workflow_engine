import { useState } from 'react';
import { Popover, ActionIcon, Tooltip, Stack, Text, Group, Badge, Button, TextInput, Select, Divider, ActionIcon as IconButton, Loader, Center, Alert, Box, ScrollArea } from '@mantine/core';
import { notifications } from '@mantine/notifications';
import { Shield, Plus, Trash2, Edit, AlertCircle } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { useRequest } from 'ahooks';
import { getCredentials, createCredential, deleteCredential, updateCredential, getCredentialTypes } from '../../services/api.ts';
import { useWorkflowStore } from '../../stores/workflowStore.ts';
import type { CredentialDto, CredentialTypeDefinition } from '../../types/workflow.ts';

const defaultTypeOptions: CredentialTypeDefinition[] = [
  { name: 'apiKey', displayName: 'API Key', fields: [] },
  { name: 'oauth2', displayName: 'OAuth2', fields: [] },
  { name: 'basicAuth', displayName: 'Basic Auth', fields: [] },
  { name: 'connectionString', displayName: 'Connection String', fields: [] },
];

export function CredentialMenu() {
  const { t } = useTranslation(['credentialPanel', 'header']);
  const [opened, setOpened] = useState(false);
  const [showForm, setShowForm] = useState(false);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [formName, setFormName] = useState('');
  const [formType, setFormType] = useState('apiKey');
  const [formFields, setFormFields] = useState<{ id: string; key: string; value: string }[]>([{ id: crypto.randomUUID(), key: '', value: '' }]);

  const { data: credentials = [], loading, error, refresh: refreshCredentials } = useRequest(
    getCredentials,
    { ready: opened },
  );

  const { data: typeOptions = defaultTypeOptions } = useRequest(getCredentialTypes);

  const { run: runCreate, loading: creating } = useRequest(
    async (payload: { name: string; type: string; fields: Record<string, string> }) => {
      await createCredential(payload);
    },
    {
      manual: true,
      onSuccess: async () => {
        resetForm();
        await refreshCredentials();
        useWorkflowStore.getState().bumpCredentialRevision();
      },
      onError: (err) => {
        notifications.show({
          title: t('credentialPanel:error'),
          message: err instanceof Error ? err.message : t('credentialPanel:createFailed'),
          color: 'red',
        });
      },
    },
  );

  const { run: runDelete } = useRequest(
    async (id: string) => {
      await deleteCredential(id);
    },
    {
      manual: true,
      onSuccess: async () => {
        await refreshCredentials();
        useWorkflowStore.getState().bumpCredentialRevision();
      },
      onError: (err) => {
        notifications.show({
          title: t('credentialPanel:error'),
          message: err instanceof Error ? err.message : t('credentialPanel:deleteFailed'),
          color: 'red',
        });
      },
    },
  );

  const { run: runUpdate, loading: updating } = useRequest(
    async (id: string, payload: { name: string; fields: Record<string, string> }) => {
      await updateCredential(id, payload);
    },
    {
      manual: true,
      onSuccess: async () => {
        resetForm();
        await refreshCredentials();
        useWorkflowStore.getState().bumpCredentialRevision();
      },
      onError: (err) => {
        notifications.show({
          title: t('credentialPanel:error'),
          message: err instanceof Error ? err.message : t('credentialPanel:updateFailed'),
          color: 'red',
        });
      },
    },
  );

  const resetForm = () => {
    setShowForm(false);
    setEditingId(null);
    setFormName('');
    setFormType('apiKey');
    setFormFields([{ id: crypto.randomUUID(), key: '', value: '' }]);
  };

  const handleCreate = () => {
    const fields: Record<string, string> = {};
    for (const f of formFields) {
      if (f.key.trim()) fields[f.key.trim()] = f.value;
    }
    runCreate({ name: formName, type: formType, fields });
  };

  const handleDelete = (id: string) => {
    if (!confirm(t('credentialPanel:deleteConfirm'))) return;
    runDelete(id);
  };

  const handleEdit = (cred: CredentialDto) => {
    setEditingId(cred.id);
    setFormName(cred.name);
    setFormType(cred.type);
    const existing = Object.entries(cred.fields ?? {}).map(([key, value]) => ({ id: crypto.randomUUID(), key, value }));
    setFormFields(existing.length > 0 ? existing : [{ id: crypto.randomUUID(), key: '', value: '' }]);
    setShowForm(true);
  };

  const handleUpdate = () => {
    if (!editingId) return;
    const fields: Record<string, string> = {};
    for (const f of formFields) {
      if (f.key.trim()) fields[f.key.trim()] = f.value;
    }
    runUpdate(editingId, { name: formName, fields });
  };

  return (
    <Popover opened={opened} onChange={setOpened} width={360} position="bottom-end" shadow="md">
      <Popover.Target>
        <Tooltip label={t('header:credentials')}>
          <ActionIcon
            variant="subtle"
            color="gray"
            size="sm"
            onClick={() => setOpened((o) => !o)}
            aria-label={t('header:credentials')}
          >
            <Shield size={16} />
          </ActionIcon>
        </Tooltip>
      </Popover.Target>
      <Popover.Dropdown>
        {showForm ? (
          <Stack gap="sm">
            <Text size="sm" fw={600}>{editingId ? t('credentialPanel:form.update') : t('credentialPanel:form.create')}</Text>
            <TextInput
              label={t('credentialPanel:form.name')}
              value={formName}
              onChange={(e) => setFormName(e.target.value)}
              size="sm"
            />
            <Select
              label={t('credentialPanel:form.type')}
              value={formType}
              onChange={(v) => setFormType(v ?? 'apiKey')}
              data={typeOptions.map((opt) => ({ label: opt.displayName, value: opt.name }))}
              size="sm"
              disabled={!!editingId}
            />
            <Divider label={t('credentialPanel:form.fields')} labelPosition="center" />
            {formFields.map((field, index) => (
              <Group key={field.id} gap="xs">
                <TextInput
                  placeholder={t('credentialPanel:form.keyPlaceholder')}
                  value={field.key}
                  onChange={(e) => {
                    const next = [...formFields];
                    next[index] = { ...next[index], key: e.target.value };
                    setFormFields(next);
                  }}
                  size="sm"
                  style={{ flex: 1 }}
                />
                <TextInput
                  placeholder={t('credentialPanel:form.valuePlaceholder')}
                  value={field.value}
                  onChange={(e) => {
                    const next = [...formFields];
                    next[index] = { ...next[index], value: e.target.value };
                    setFormFields(next);
                  }}
                  size="sm"
                  style={{ flex: 1 }}
                />
                <IconButton
                  color="red"
                  variant="subtle"
                  size="sm"
                  onClick={() => setFormFields(formFields.filter((_, i) => i !== index))}
                >
                  <Trash2 size={14} />
                </IconButton>
              </Group>
            ))}
            <Button
              variant="subtle"
              size="xs"
              leftSection={<Plus size={14} />}
              onClick={() => setFormFields([...formFields, { id: crypto.randomUUID(), key: '', value: '' }])}
            >
              {t('credentialPanel:form.addField')}
            </Button>
            <Group justify="flex-end">
              <Button variant="default" size="xs" onClick={resetForm}>
                {t('credentialPanel:form.cancel')}
              </Button>
              <Button size="xs" loading={creating || updating} onClick={editingId ? handleUpdate : handleCreate}>
                {editingId ? t('credentialPanel:form.update') : t('credentialPanel:form.create')}
              </Button>
            </Group>
          </Stack>
        ) : (
          <Stack gap="xs">
            <Group justify="space-between">
              <Text size="sm" fw={600}>{t('credentialPanel:title')}</Text>
              <Button
                size="xs"
                variant="light"
                leftSection={<Plus size={12} />}
                onClick={() => { setShowForm(true); setEditingId(null); setFormName(''); setFormType('apiKey'); setFormFields([{ id: crypto.randomUUID(), key: '', value: '' }]); }}
              >
                {t('credentialPanel:addButton')}
              </Button>
            </Group>
            {loading ? (
              <Center py="md"><Loader size="sm" /></Center>
            ) : error ? (
              <Alert icon={<AlertCircle size={14} />} title={t('credentialPanel:error')} color="red">
                {error.message ?? t('credentialPanel:loadFailed')}
              </Alert>
            ) : credentials.length === 0 ? (
              <Text c="dimmed" size="xs" ta="center" py="md">{t('credentialPanel:empty')}</Text>
            ) : (
              <ScrollArea.Autosize mah={300}>
                <Stack gap={2}>
                  {credentials.map((cred) => (
                    <Group
                      key={cred.id}
                      p={6}
                      gap="xs"
                      style={{ borderRadius: 4, border: '1px solid var(--mantine-color-gray-3)' }}
                    >
                      <Box style={{ flex: 1, minWidth: 0 }}>
                        <Text size="xs" fw={500} truncate>{cred.name}</Text>
                        <Group gap={4}>
                          <Badge size="xs" variant="light">{cred.type}</Badge>
                          <Text size="xs" c="dimmed">{new Date(cred.createdAt).toLocaleDateString()}</Text>
                        </Group>
                      </Box>
                      <IconButton size="sm" variant="subtle" onClick={() => handleEdit(cred)}>
                        <Edit size={14} />
                      </IconButton>
                      <IconButton size="sm" variant="subtle" color="red" onClick={() => handleDelete(cred.id)}>
                        <Trash2 size={14} />
                      </IconButton>
                    </Group>
                  ))}
                </Stack>
              </ScrollArea.Autosize>
            )}
          </Stack>
        )}
      </Popover.Dropdown>
    </Popover>
  );
}

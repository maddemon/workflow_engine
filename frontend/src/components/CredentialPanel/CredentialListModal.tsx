import { useState, useEffect } from 'react';
import { notifications } from '@mantine/notifications';
import { Modal, Stack, Text, Table, ActionIcon, Button, Group, TextInput, Select, Badge, Divider, Loader, Center, Alert } from '@mantine/core';
import { useTranslation } from 'react-i18next';
import { Plus, Trash2, Edit, AlertCircle } from 'lucide-react';
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

interface CredentialListModalProps {
  opened: boolean;
  onClose: () => void;
}

export function CredentialListModal({ opened, onClose }: CredentialListModalProps) {
  const { t } = useTranslation('credentialPanel');
  const [showForm, setShowForm] = useState(false);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [formName, setFormName] = useState('');
  const [formType, setFormType] = useState('apiKey');
  const [formFields, setFormFields] = useState<{ id: string; key: string; value: string }[]>([{ id: crypto.randomUUID(), key: '', value: '' }]);

  const { data: credentials = [], loading, error, refresh: refreshCredentials } = useRequest(
    getCredentials,
    { ready: opened },
  );

  /* eslint-disable react-hooks/set-state-in-effect */
  useEffect(() => {
    if (opened) {
      setShowForm(false);
      setEditingId(null);
    }
  }, [opened]);
  /* eslint-enable react-hooks/set-state-in-effect */

  const { data: typeOptions = defaultTypeOptions } = useRequest(getCredentialTypes);

  const handleCreate = async () => {
    const fields: Record<string, string> = {};
    for (const f of formFields) {
      if (f.key.trim()) {
        fields[f.key.trim()] = f.value;
      }
    }
    try {
      await createCredential({ name: formName, type: formType, fields });
      setShowForm(false);
      setFormName('');
      setFormType('apiKey');
      setFormFields([{ id: crypto.randomUUID(), key: '', value: '' }]);
      await refreshCredentials();
      useWorkflowStore.getState().bumpCredentialRevision();
    } catch (err) {
      notifications.show({
        title: t('error'),
        message: err instanceof Error ? err.message : t('createFailed'),
        color: 'red',
      });
    }
  };

  const handleDelete = async (id: string) => {
    if (!confirm(t('deleteConfirm'))) return;
    try {
      await deleteCredential(id);
      await refreshCredentials();
      useWorkflowStore.getState().bumpCredentialRevision();
    } catch (err) {
      notifications.show({
        title: t('error'),
        message: err instanceof Error ? err.message : t('deleteFailed'),
        color: 'red',
      });
    }
  };

  const handleEdit = (cred: CredentialDto) => {
    setEditingId(cred.id);
    setFormName(cred.name);
    setFormType(cred.type);
    const existing = Object.entries(cred.fields ?? {}).map(([key, value]) => ({ id: crypto.randomUUID(), key, value }));
    setFormFields(existing.length > 0 ? existing : [{ id: crypto.randomUUID(), key: '', value: '' }]);
    setShowForm(true);
  };

  const handleUpdate = async () => {
    if (!editingId) return;
    const fields: Record<string, string> = {};
    for (const f of formFields) {
      if (f.key.trim()) {
        fields[f.key.trim()] = f.value;
      }
    }
    try {
      await updateCredential(editingId, { name: formName, fields });
      setShowForm(false);
      setEditingId(null);
      setFormName('');
      setFormType('apiKey');
      setFormFields([{ id: crypto.randomUUID(), key: '', value: '' }]);
      await refreshCredentials();
      useWorkflowStore.getState().bumpCredentialRevision();
    } catch (err) {
      notifications.show({
        title: t('error'),
        message: err instanceof Error ? err.message : t('updateFailed'),
        color: 'red',
      });
    }
  };

  return (
    <Modal opened={opened} onClose={onClose} title={t('title')} size="lg">
      {showForm ? (
        <Stack gap="sm">
          <TextInput
            label={t('form.name')}
            value={formName}
            onChange={(e) => setFormName(e.target.value)}
            size="sm"
          />
          <Select
            label={t('form.type')}
            value={formType}
            onChange={(v) => setFormType(v ?? 'apiKey')}
            data={typeOptions.map((opt) => ({ label: opt.displayName, value: opt.name }))}
            size="sm"
          />
          <Divider label={t('form.fields')} labelPosition="center" />
          {formFields.map((field, index) => (
            <Group key={field.id} gap="xs">
              <TextInput
                placeholder={t('form.keyPlaceholder')}
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
                placeholder={t('form.valuePlaceholder')}
                value={field.value}
                onChange={(e) => {
                  const next = [...formFields];
                  next[index] = { ...next[index], value: e.target.value };
                  setFormFields(next);
                }}
                size="sm"
                style={{ flex: 1 }}
              />
              <ActionIcon
                color="red"
                variant="subtle"
                onClick={() => setFormFields(formFields.filter((_, i) => i !== index))}
              >
                <Trash2 size={14} />
              </ActionIcon>
            </Group>
          ))}
          <Button
            variant="subtle"
            size="xs"
            leftSection={<Plus size={14} />}
            onClick={() => setFormFields([...formFields, { id: crypto.randomUUID(), key: '', value: '' }])}
          >
            {t('form.addField')}
          </Button>
          <Group justify="flex-end">
            <Button variant="default" onClick={() => { setShowForm(false); setEditingId(null); }}>
              {t('form.cancel')}
            </Button>
            <Button onClick={editingId ? handleUpdate : handleCreate}>
              {editingId ? t('form.update') : t('form.create')}
            </Button>
          </Group>
        </Stack>
      ) : loading ? (
        <Center py="md"><Loader size="sm" /></Center>
      ) : error ? (
        <Alert icon={<AlertCircle size={16} />} title={t('error')} color="red">
          {error.message ?? t('loadFailed')}
        </Alert>
      ) : (
        <Stack gap="sm">
          <Button
            size="xs"
            leftSection={<Plus size={14} />}
            onClick={() => { setShowForm(true); setEditingId(null); setFormName(''); setFormType('apiKey'); setFormFields([{ id: crypto.randomUUID(), key: '', value: '' }]); }}
          >
            {t('addButton')}
          </Button>
          {credentials.length === 0 ? (
            <Text c="dimmed" size="sm" ta="center" py="md">{t('empty')}</Text>
          ) : (
            <Table>
              <Table.Thead>
                <Table.Tr>
                  <Table.Th>{t('table.name')}</Table.Th>
                  <Table.Th>{t('table.type')}</Table.Th>
                  <Table.Th>{t('table.created')}</Table.Th>
                  <Table.Th />
                </Table.Tr>
              </Table.Thead>
              <Table.Tbody>
                {credentials.map((cred) => (
                  <Table.Tr key={cred.id}>
                    <Table.Td>{cred.name}</Table.Td>
                    <Table.Td><Badge size="xs" variant="light">{cred.type}</Badge></Table.Td>
                    <Table.Td>{new Date(cred.createdAt).toLocaleDateString()}</Table.Td>
                    <Table.Td>
                      <Group gap={4} justify="flex-end">
                        <ActionIcon size="sm" variant="subtle" onClick={() => handleEdit(cred)}>
                          <Edit size={14} />
                        </ActionIcon>
                        <ActionIcon size="sm" variant="subtle" color="red" onClick={() => handleDelete(cred.id)}>
                          <Trash2 size={14} />
                        </ActionIcon>
                      </Group>
                    </Table.Td>
                  </Table.Tr>
                ))}
              </Table.Tbody>
            </Table>
          )}
        </Stack>
      )}
    </Modal>
  );
}

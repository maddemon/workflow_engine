import { useState, useEffect } from 'react';
import { notifications } from '@mantine/notifications';
import { Modal, Stack, Text, Table, ActionIcon, Button, Group, TextInput, Select, Badge, Divider, Loader, Center, Alert } from '@mantine/core';
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
        title: 'Error',
        message: err instanceof Error ? err.message : 'Failed to create credential',
        color: 'red',
      });
    }
  };

  const handleDelete = async (id: string) => {
    if (!confirm('Delete this credential?')) return;
    try {
      await deleteCredential(id);
      await refreshCredentials();
      useWorkflowStore.getState().bumpCredentialRevision();
    } catch (err) {
      notifications.show({
        title: 'Error',
        message: err instanceof Error ? err.message : 'Failed to delete credential',
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
        title: 'Error',
        message: err instanceof Error ? err.message : 'Failed to update credential',
        color: 'red',
      });
    }
  };

  return (
    <Modal opened={opened} onClose={onClose} title="Credentials" size="lg">
      {showForm ? (
        <Stack gap="sm">
          <TextInput
            label="Name"
            value={formName}
            onChange={(e) => setFormName(e.target.value)}
            size="sm"
          />
          <Select
            label="Type"
            value={formType}
            onChange={(v) => setFormType(v ?? 'apiKey')}
            data={typeOptions.map((t) => ({ label: t.displayName, value: t.name }))}
            size="sm"
          />
          <Divider label="Fields" labelPosition="center" />
          {formFields.map((field, index) => (
            <Group key={field.id} gap="xs">
              <TextInput
                placeholder="Key"
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
                placeholder="Value"
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
            Add Field
          </Button>
          <Group justify="flex-end">
            <Button variant="default" onClick={() => { setShowForm(false); setEditingId(null); }}>
              Cancel
            </Button>
            <Button onClick={editingId ? handleUpdate : handleCreate}>
              {editingId ? 'Update' : 'Create'}
            </Button>
          </Group>
        </Stack>
      ) : loading ? (
        <Center py="md"><Loader size="sm" /></Center>
      ) : error ? (
        <Alert icon={<AlertCircle size={16} />} title="Error" color="red">
          {error.message ?? 'Failed to load credentials'}
        </Alert>
      ) : (
        <Stack gap="sm">
          <Button
            size="xs"
            leftSection={<Plus size={14} />}
            onClick={() => { setShowForm(true); setEditingId(null); setFormName(''); setFormType('apiKey'); setFormFields([{ id: crypto.randomUUID(), key: '', value: '' }]); }}
          >
            Add Credential
          </Button>
          {credentials.length === 0 ? (
            <Text c="dimmed" size="sm" ta="center" py="md">No credentials yet.</Text>
          ) : (
            <Table>
              <Table.Thead>
                <Table.Tr>
                  <Table.Th>Name</Table.Th>
                  <Table.Th>Type</Table.Th>
                  <Table.Th>Created</Table.Th>
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

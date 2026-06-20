import { useState, useEffect, useCallback } from 'react';
import { Modal, Stack, Text, Table, ActionIcon, Button, Group, TextInput, Select, Badge, Divider, Loader, Center } from '@mantine/core';
import { Plus, Trash2, Edit } from 'lucide-react';
import { getCredentials, createCredential, deleteCredential, updateCredential } from '../../services/api.ts';
import type { CredentialDto } from '../../types/workflow.ts';

interface CredentialListModalProps {
  opened: boolean;
  onClose: () => void;
}

export function CredentialListModal({ opened, onClose }: CredentialListModalProps) {
  const [credentials, setCredentials] = useState<CredentialDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [showForm, setShowForm] = useState(false);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [formName, setFormName] = useState('');
  const [formType, setFormType] = useState('apiKey');
  const [formFields, setFormFields] = useState<{ key: string; value: string }[]>([{ key: '', value: '' }]);

  const loadCredentials = useCallback(async () => {
    setLoading(true);
    try {
      const list = await getCredentials();
      setCredentials(list);
    } catch {
      // ignore
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    if (opened) {
      loadCredentials();
      setShowForm(false);
      setEditingId(null);
    }
  }, [opened, loadCredentials]);

  const handleCreate = async () => {
    const fields: Record<string, string> = {};
    for (const f of formFields) {
      if (f.key.trim()) {
        fields[f.key.trim()] = f.value;
      }
    }
    try {
      await createCredential({ name: formName, type: formType, data: fields });
      setShowForm(false);
      setFormName('');
      setFormType('apiKey');
      setFormFields([{ key: '', value: '' }]);
      await loadCredentials();
    } catch (err) {
      alert(err instanceof Error ? err.message : 'Failed to create credential');
    }
  };

  const handleDelete = async (id: string) => {
    if (!confirm('Delete this credential?')) return;
    try {
      await deleteCredential(id);
      await loadCredentials();
    } catch (err) {
      alert(err instanceof Error ? err.message : 'Failed to delete credential');
    }
  };

  const handleEdit = (cred: CredentialDto) => {
    setEditingId(cred.id);
    setFormName(cred.name);
    setFormType(cred.type);
    setFormFields([{ key: '', value: '' }]);
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
      await updateCredential(editingId, { name: formName, type: formType, data: fields });
      setShowForm(false);
      setEditingId(null);
      setFormName('');
      setFormType('apiKey');
      setFormFields([{ key: '', value: '' }]);
      await loadCredentials();
    } catch (err) {
      alert(err instanceof Error ? err.message : 'Failed to update credential');
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
            data={[
              { label: 'API Key', value: 'apiKey' },
              { label: 'OAuth2', value: 'oauth2' },
              { label: 'Basic Auth', value: 'basicAuth' },
            ]}
            size="sm"
          />
          <Divider label="Fields" labelPosition="center" />
          {formFields.map((field, index) => (
            <Group key={index} gap="xs">
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
            onClick={() => setFormFields([...formFields, { key: '', value: '' }])}
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
      ) : (
        <Stack gap="sm">
          <Button
            size="xs"
            leftSection={<Plus size={14} />}
            onClick={() => { setShowForm(true); setEditingId(null); setFormName(''); setFormType('apiKey'); setFormFields([{ key: '', value: '' }]); }}
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

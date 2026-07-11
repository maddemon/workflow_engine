import { useCallback, useEffect, useState } from 'react';
import { notifications } from '@mantine/notifications';
import { Select, Group, Text, Button, Modal, Stack, TextInput, ActionIcon } from '@mantine/core';
import { Plus, Trash2 } from 'lucide-react';
import { InfoTooltip } from './InfoTooltip.tsx';
import { getCredentials, createCredential, getCredentialTypes } from '../../../services/api.ts';
import { useWorkflowStore } from '../../../stores/workflowStore.ts';
import type { CredentialDto, ParameterDefinition, CredentialTypeDefinition } from '../../../types/workflow.ts';

interface CredentialFieldProps {
  definition: ParameterDefinition;
  value: unknown;
  onChange: (value: string) => void;
  error?: string;
}

export function CredentialField({ definition, value, onChange, error }: CredentialFieldProps) {
  const [credentials, setCredentials] = useState<CredentialDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [types, setTypes] = useState<CredentialTypeDefinition[]>([]);
  const credentialRevision = useWorkflowStore((s) => s.credentialRevision);
  const bumpCredentialRevision = useWorkflowStore((s) => s.bumpCredentialRevision);

  const [createOpen, setCreateOpen] = useState(false);
  const [formName, setFormName] = useState('');
  const [formType, setFormType] = useState('apiKey');
  const [formFields, setFormFields] = useState<{ key: string; value: string }[]>([{ key: '', value: '' }]);

  const load = useCallback(() => {
    let cancelled = false;
    setLoading(true);
    getCredentials()
      .then((data) => {
        if (cancelled) return;
        const filtered = definition.credentialType
          ? data.filter((c) => c.type === definition.credentialType)
          : data;
        setCredentials(filtered);
        setLoading(false);
      })
      .catch(() => {
        if (!cancelled) {
          setCredentials([]);
          setLoading(false);
        }
      });
    return () => {
      cancelled = true;
    };
  }, [definition.credentialType]);

  useEffect(() => {
    load();
  }, [load, credentialRevision]);

  useEffect(() => {
    getCredentialTypes()
      .then(setTypes)
      .catch(() => setTypes([]));
  }, []);

  const typeOptions = types.length > 0
    ? types.map((t) => ({ label: t.displayName, value: t.name }))
    : [
        { label: 'API Key', value: 'apiKey' },
        { label: 'OAuth2', value: 'oauth2' },
        { label: 'Basic Auth', value: 'basicAuth' },
        { label: 'Connection String', value: 'connectionString' },
      ];

  const handleCreate = async () => {
    if (!formName.trim()) {
      notifications.show({
        title: 'Error',
        message: 'Credential name is required.',
        color: 'red',
      });
      return;
    }
    const fields: Record<string, string> = {};
    for (const f of formFields) {
      if (f.key.trim()) fields[f.key.trim()] = f.value;
    }
    try {
      await createCredential({ name: formName, type: formType, fields });
      setCreateOpen(false);
      setFormName('');
      setFormType('apiKey');
      setFormFields([{ key: '', value: '' }]);
      bumpCredentialRevision();
    } catch (err) {
      notifications.show({
        title: 'Error',
        message: err instanceof Error ? err.message : 'Failed to create credential',
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
          onClick={() => setCreateOpen(true)}
          disabled={loading}
        >
          New
        </Button>
      </Group>
      <Select
        error={error}
        value={String(value ?? '')}
        onChange={(v) => onChange(v ?? '')}
        placeholder="-- Select Credential --"
        data={credentials.map((c) => ({ label: `${c.name} (${c.type})`, value: c.id }))}
        searchable
        disabled={loading}
      />
      <Modal opened={createOpen} onClose={() => setCreateOpen(false)} title="New Credential" size="lg">
        <Stack gap="sm">
          <TextInput label="Name" value={formName} onChange={(e) => setFormName(e.target.value)} size="sm" />
          <Select
            label="Type"
            value={formType}
            onChange={(v) => setFormType(v ?? 'apiKey')}
            data={typeOptions}
            size="sm"
          />
          <Stack gap="xs">
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
          </Stack>
          <Button
            variant="subtle"
            size="xs"
            leftSection={<Plus size={14} />}
            onClick={() => setFormFields([...formFields, { key: '', value: '' }])}
          >
            Add Field
          </Button>
          <Group justify="flex-end">
            <Button variant="default" onClick={() => setCreateOpen(false)}>Cancel</Button>
            <Button onClick={handleCreate}>Create</Button>
          </Group>
        </Stack>
      </Modal>
    </div>
  );
}

import { useState } from 'react';
import {
  Alert,
  Badge,
  Button,
  CopyButton,
  Group,
  InputLabel,
  Modal,
  Paper,
  Stack,
  Table,
  Text,
  TextInput,
  Title,
  Tooltip,
} from '@mantine/core';
import { notifications } from '@mantine/notifications';
import { useRequest } from 'ahooks';
import { Check, Copy, Trash2 } from 'lucide-react';
import { useAuth } from '../hooks/AuthContext.tsx';
import type { CreateApiKeyResult } from '../types/workflow.ts';
import * as api from '../services/api.ts';

export function SettingsPage() {
  const { user } = useAuth();

  // --- API Key state ---
  const {
    data: keys = [],
    loading: keysLoading,
    refresh: refreshKeys,
  } = useRequest(api.listApiKeys);

  const { runAsync: createKey } = useRequest(api.createApiKey, { manual: true });
  const { runAsync: revokeKey } = useRequest(api.revokeApiKey, { manual: true });

  // --- Create modal state ---
  const [createModalOpen, setCreateModalOpen] = useState(false);
  const [newKeyName, setNewKeyName] = useState('');
  const [newKeyExpiresAt, setNewKeyExpiresAt] = useState('');
  const [creating, setCreating] = useState(false);
  const [createdKey, setCreatedKey] = useState<CreateApiKeyResult | null>(null);

  const openCreateModal = () => {
    setNewKeyName('');
    setNewKeyExpiresAt('');
    setCreatedKey(null);
    setCreateModalOpen(true);
  };

  const handleCreate = async () => {
    if (!newKeyName.trim()) {
      notifications.show({ title: 'Validation', message: 'Key name is required', color: 'yellow' });
      return;
    }
    setCreating(true);
    try {
      const expiresAt = newKeyExpiresAt.trim() ? newKeyExpiresAt.trim() : null;
      const result = await createKey(newKeyName.trim(), expiresAt);
      setCreatedKey(result);
      await refreshKeys();
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Failed to create API key';
      notifications.show({ title: 'Error', message: msg, color: 'red' });
    } finally {
      setCreating(false);
    }
  };

  const closeCreateModal = () => {
    if (!createdKey) {
      setCreateModalOpen(false);
    }
  };

  const handleCloseAfterCreate = () => {
    setCreatedKey(null);
    setCreateModalOpen(false);
  };

  // --- Revoke state ---
  const [revokeTarget, setRevokeTarget] = useState<string | null>(null);
  const [revokeTargetName, setRevokeTargetName] = useState('');
  const [revoking, setRevoking] = useState(false);

  const confirmRevoke = (id: string, name: string) => {
    setRevokeTarget(id);
    setRevokeTargetName(name);
  };

  const handleRevoke = async () => {
    if (!revokeTarget) return;
    setRevoking(true);
    try {
      await revokeKey(revokeTarget);
      notifications.show({ title: 'Revoked', message: `API key "${revokeTargetName}" has been revoked`, color: 'green' });
      setRevokeTarget(null);
      await refreshKeys();
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Failed to revoke API key';
      notifications.show({ title: 'Error', message: msg, color: 'red' });
    } finally {
      setRevoking(false);
    }
  };

  const formatDate = (dateStr: string | null | undefined) => {
    if (!dateStr) return '—';
    try {
      return new Date(dateStr).toLocaleDateString();
    } catch {
      return dateStr;
    }
  };

  return (
    <div style={{ height: '100%', overflowY: 'auto' }}>
      <Stack p="md" gap="lg" style={{ maxWidth: 860, margin: '0 auto' }}>
        <Title order={2}>Settings</Title>

        {/* --- User Info Section --- */}
        <Paper p="md" withBorder>
          <Title order={4} mb="sm">User Info</Title>
          <Stack gap="sm">
            <TextInput
              label="Email"
              value={user?.email ?? ''}
              disabled
              readOnly
            />
            <TextInput
              label="User Name"
              value={user?.userName ?? ''}
              disabled
              readOnly
            />
            <TextInput
              label="Display Name"
              value={user?.displayName ?? ''}
              disabled
              readOnly
            />
            <TextInput
              label="Created At"
              value={formatDate(user?.createdAt)}
              disabled
              readOnly
            />
            <div>
              <InputLabel>Roles</InputLabel>
              <Group gap="xs" mt={4}>
                {(user?.roles ?? []).length === 0 ? (
                  <Text size="sm" c="dimmed">No roles assigned</Text>
                ) : (
                  (user?.roles ?? []).map((role) => (
                    <Badge key={role} variant="light" color="blue" size="sm">
                      {role}
                    </Badge>
                  ))
                )}
              </Group>
            </div>
          </Stack>
        </Paper>

        {/* --- API Key Management Section --- */}
        <Paper p="md" withBorder>
          <Group justify="space-between" mb="sm">
            <Title order={4}>API Keys</Title>
            <Button size="compact-sm" onClick={openCreateModal}>
              Create API Key
            </Button>
          </Group>

          {keysLoading ? (
            <Text size="sm" c="dimmed">Loading API keys…</Text>
          ) : keys.length === 0 ? (
            <Text size="sm" c="dimmed">No API keys yet. Create one to get started.</Text>
          ) : (
            <Table striped highlightOnHover>
              <Table.Thead>
                <Table.Tr>
                  <Table.Th>Name</Table.Th>
                  <Table.Th>Prefix</Table.Th>
                  <Table.Th>Created</Table.Th>
                  <Table.Th>Expires</Table.Th>
                  <Table.Th>Status</Table.Th>
                  <Table.Th style={{ width: 80 }}>Actions</Table.Th>
                </Table.Tr>
              </Table.Thead>
              <Table.Tbody>
                {keys.map((key) => {
                  const isRevoked = !!key.revokedAt;
                  const isExpired = !isRevoked && key.expiresAt && new Date(key.expiresAt) < new Date();
                  return (
                    <Table.Tr key={key.id} opacity={isRevoked ? 0.5 : undefined}>
                      <Table.Td>
                        <Text size="sm" td={isRevoked ? 'line-through' : undefined}>
                          {key.name}
                        </Text>
                      </Table.Td>
                      <Table.Td>
                        <Text size="sm" ff="monospace">
                          {key.prefix}
                        </Text>
                      </Table.Td>
                      <Table.Td>
                        <Text size="sm">{formatDate(key.createdAt)}</Text>
                      </Table.Td>
                      <Table.Td>
                        <Text size="sm">{formatDate(key.expiresAt)}</Text>
                      </Table.Td>
                      <Table.Td>
                        {isRevoked ? (
                          <Badge variant="light" color="gray" size="sm">Revoked</Badge>
                        ) : isExpired ? (
                          <Badge variant="light" color="orange" size="sm">Expired</Badge>
                        ) : (
                          <Badge variant="light" color="green" size="sm">Active</Badge>
                        )}
                      </Table.Td>
                      <Table.Td>
                        {!isRevoked && (
                          <Tooltip label="Revoke key">
                            <Button
                              size="compact-xs"
                              variant="subtle"
                              color="red"
                              onClick={() => confirmRevoke(key.id, key.name)}
                            >
                              <Trash2 size={14} />
                            </Button>
                          </Tooltip>
                        )}
                      </Table.Td>
                    </Table.Tr>
                  );
                })}
              </Table.Tbody>
            </Table>
          )}
        </Paper>
      </Stack>

      {/* --- Create API Key Modal --- */}
      <Modal
        opened={createModalOpen}
        onClose={closeCreateModal}
        title="Create API Key"
        size="md"
      >
        <Stack gap="md">
          {createdKey ? (
            <>
              <Alert color="green" title="API Key Created">
                <Text size="sm" mb="sm">
                  Copy this key now. It will <strong>not</strong> be shown again.
                </Text>
                <Text size="sm" ff="monospace" mb="sm" p="xs" bg="gray.0" style={{ borderRadius: 4, wordBreak: 'break-all' }}>
                  {createdKey.key}
                </Text>
                <CopyButton value={createdKey.key}>
                  {({ copied, copy }) => (
                    <Button
                      size="compact-sm"
                      variant="light"
                      color={copied ? 'green' : 'blue'}
                      leftSection={copied ? <Check size={14} /> : <Copy size={14} />}
                      onClick={copy}
                    >
                      {copied ? 'Copied' : 'Copy to clipboard'}
                    </Button>
                  )}
                </CopyButton>
              </Alert>
              <Button variant="subtle" onClick={handleCloseAfterCreate}>
                Close
              </Button>
            </>
          ) : (
            <>
              <TextInput
                label="Key Name"
                placeholder="e.g. My API Key"
                required
                value={newKeyName}
                onChange={(e) => setNewKeyName(e.currentTarget.value)}
              />
              <TextInput
                label="Expires At (optional)"
                placeholder="YYYY-MM-DD"
                value={newKeyExpiresAt}
                onChange={(e) => setNewKeyExpiresAt(e.currentTarget.value)}
              />
              <Group justify="flex-end" gap="sm">
                <Button variant="default" onClick={() => setCreateModalOpen(false)}>
                  Cancel
                </Button>
                <Button onClick={handleCreate} loading={creating}>
                  Create
                </Button>
              </Group>
            </>
          )}
        </Stack>
      </Modal>

      {/* --- Revoke Confirm Modal --- */}
      <Modal
        opened={!!revokeTarget}
        onClose={() => setRevokeTarget(null)}
        title="Revoke API Key"
        size="sm"
      >
        <Stack gap="md">
          <Text size="sm">
            Are you sure you want to revoke the API key <strong>{revokeTargetName}</strong>?
            This action cannot be undone. Any services using this key will lose access immediately.
          </Text>
          <Group justify="flex-end" gap="sm">
            <Button variant="default" onClick={() => setRevokeTarget(null)}>
              Cancel
            </Button>
            <Button color="red" onClick={handleRevoke} loading={revoking}>
              Revoke
            </Button>
          </Group>
        </Stack>
      </Modal>
    </div>
  );
}

import { useState } from "react"
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
} from "@mantine/core"
import { notifications } from "@mantine/notifications"
import { useRequest } from "ahooks"
import { Check, Copy, Trash2 } from "lucide-react"
import { Trans, useTranslation } from "react-i18next"
import { useAuth } from "../hooks/AuthContext.tsx"
import type { CreateApiKeyResult } from "../types/workflow.ts"
import * as api from "../services/api.ts"

export function SettingsPage() {
  const { user } = useAuth()
  const { t } = useTranslation(['settings', 'common'])

  // --- API Key state ---
  const { data: keys = [], loading: keysLoading, refresh: refreshKeys } = useRequest(api.listApiKeys)

  const { runAsync: createKey } = useRequest(api.createApiKey, { manual: true })
  const { runAsync: revokeKey } = useRequest(api.revokeApiKey, { manual: true })

  // --- Create modal state ---
  const [createModalOpen, setCreateModalOpen] = useState(false)
  const [newKeyName, setNewKeyName] = useState("")
  const [newKeyExpiresAt, setNewKeyExpiresAt] = useState("")
  const [creating, setCreating] = useState(false)
  const [createdKey, setCreatedKey] = useState<CreateApiKeyResult | null>(null)

  const openCreateModal = () => {
    setNewKeyName("")
    setNewKeyExpiresAt("")
    setCreatedKey(null)
    setCreateModalOpen(true)
  }

  const handleCreate = async () => {
    if (!newKeyName.trim()) {
      notifications.show({ title: t('notification.validationTitle'), message: t('apiKeys.nameRequired'), color: "yellow" })
      return
    }
    setCreating(true)
    try {
      const expiresAt = newKeyExpiresAt.trim() ? newKeyExpiresAt.trim() : null
      const result = await createKey(newKeyName.trim(), expiresAt)
      setCreatedKey(result)
      await refreshKeys()
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : t('apiKeys.createFailed')
      notifications.show({ title: t('error'), message: msg, color: "red" })
    } finally {
      setCreating(false)
    }
  }

  const closeCreateModal = () => {
    if (!createdKey) {
      setCreateModalOpen(false)
    }
  }

  const handleCloseAfterCreate = () => {
    setCreatedKey(null)
    setCreateModalOpen(false)
  }

  // --- Revoke state ---
  const [revokeTarget, setRevokeTarget] = useState<string | null>(null)
  const [revokeTargetName, setRevokeTargetName] = useState("")
  const [revoking, setRevoking] = useState(false)

  const confirmRevoke = (id: string, name: string) => {
    setRevokeTarget(id)
    setRevokeTargetName(name)
  }

  const handleRevoke = async () => {
    if (!revokeTarget) return
    setRevoking(true)
    try {
      await revokeKey(revokeTarget)
      notifications.show({
        title: t('apiKeys.revokeSuccessTitle'),
        message: t('apiKeys.revokeSuccess', { name: revokeTargetName }),
        color: "green",
      })
      setRevokeTarget(null)
      await refreshKeys()
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : t('apiKeys.revokeFailed')
      notifications.show({ title: t('error'), message: msg, color: "red" })
    } finally {
      setRevoking(false)
    }
  }

  const formatDate = (dateStr: string | null | undefined) => {
    if (!dateStr) return "—"
    try {
      return new Date(dateStr).toLocaleDateString()
    } catch {
      return dateStr
    }
  }

  return (
    <div style={{ height: "100%", overflowY: "auto" }}>
      <Stack p="md" gap="lg" style={{ maxWidth: 860, margin: "0 auto" }}>
        <Title order={2}>{t('title')}</Title>

        {/* --- User Info Section --- */}
        <Paper p="md" withBorder>
          <Title order={4} mb="sm">
            {t('userInfo')}
          </Title>
          <Stack gap="sm">
            <TextInput label={t('email')} value={user?.email ?? ""} disabled readOnly />
            <TextInput label={t('userName')} value={user?.userName ?? ""} disabled readOnly />
            <TextInput label={t('displayName')} value={user?.displayName ?? ""} disabled readOnly />
            <TextInput label={t('createdAt')} value={formatDate(user?.createdAt)} disabled readOnly />
            <div>
              <InputLabel>{t('roles')}</InputLabel>
              <Group gap="xs" mt={4}>
                {(user?.roles ?? []).length === 0 ? (
                  <Text size="sm" c="dimmed">
                    {t('noRoles')}
                  </Text>
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
            <Title order={4}>{t('apiKeys.title')}</Title>
            <Button size="compact-sm" onClick={openCreateModal}>
              {t('apiKeys.create')}
            </Button>
          </Group>

          {keysLoading ? (
            <Text size="sm" c="dimmed">
              {t('apiKeys.loading')}
            </Text>
          ) : keys.length === 0 ? (
            <Text size="sm" c="dimmed">
              {t('apiKeys.noKeys')}
            </Text>
          ) : (
            <Table striped highlightOnHover>
              <Table.Thead>
                <Table.Tr>
                  <Table.Th>{t('apiKeys.name')}</Table.Th>
                  <Table.Th>{t('apiKeys.prefix')}</Table.Th>
                  <Table.Th>{t('apiKeys.created')}</Table.Th>
                  <Table.Th>{t('apiKeys.expires')}</Table.Th>
                  <Table.Th>{t('apiKeys.status')}</Table.Th>
                  <Table.Th style={{ width: 80 }}>{t('apiKeys.actions')}</Table.Th>
                </Table.Tr>
              </Table.Thead>
              <Table.Tbody>
                {keys.map((key) => {
                  const isRevoked = !!key.revokedAt
                  const isExpired = !isRevoked && key.expiresAt && new Date(key.expiresAt) < new Date()
                  return (
                    <Table.Tr key={key.id} opacity={isRevoked ? 0.5 : undefined}>
                      <Table.Td>
                        <Text size="sm" td={isRevoked ? "line-through" : undefined}>
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
                          <Badge variant="light" color="gray" size="sm">
                            {t('apiKeys.revoked')}
                          </Badge>
                        ) : isExpired ? (
                          <Badge variant="light" color="orange" size="sm">
                            {t('apiKeys.expired')}
                          </Badge>
                        ) : (
                          <Badge variant="light" color="green" size="sm">
                            {t('apiKeys.active')}
                          </Badge>
                        )}
                      </Table.Td>
                      <Table.Td>
                        {!isRevoked && (
                          <Tooltip label={t('apiKeys.revoke')}>
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
                  )
                })}
              </Table.Tbody>
            </Table>
          )}
        </Paper>
      </Stack>

      {/* --- Create API Key Modal --- */}
      <Modal opened={createModalOpen} onClose={closeCreateModal} title={t('apiKeys.create')} size="md">
        <Stack gap="md">
          {createdKey ? (
            <>
              <Alert color="green" title={t('apiKeys.keyCreatedTitle')}>
                <Text size="sm" mb="sm">
                  <Trans i18nKey="settings:apiKeys.keyCreated" components={{ strong: <strong /> }} />
                </Text>
                <Text
                  size="sm"
                  ff="monospace"
                  mb="sm"
                  p="xs"
                  bg="gray.0"
                  style={{ borderRadius: 4, wordBreak: "break-all" }}
                >
                  {createdKey.key}
                </Text>
                <CopyButton value={createdKey.key}>
                  {({ copied, copy }) => (
                    <Button
                      size="compact-sm"
                      variant="light"
                      color={copied ? "green" : "blue"}
                      leftSection={copied ? <Check size={14} /> : <Copy size={14} />}
                      onClick={copy}
                    >
                      {copied ? t('apiKeys.copied') : t('apiKeys.copyToClipboard')}
                    </Button>
                  )}
                </CopyButton>
              </Alert>
              <Button variant="subtle" onClick={handleCloseAfterCreate}>
                {t('common:close')}
              </Button>
            </>
          ) : (
            <>
              <TextInput
                label={t('apiKeys.keyName')}
                placeholder={t('apiKeys.keyNamePlaceholder')}
                required
                value={newKeyName}
                onChange={(e) => setNewKeyName(e.currentTarget.value)}
              />
              <TextInput
                label={t('apiKeys.expiresAt')}
                placeholder={t('apiKeys.expiresAtPlaceholder')}
                value={newKeyExpiresAt}
                onChange={(e) => setNewKeyExpiresAt(e.currentTarget.value)}
              />
              <Group justify="flex-end" gap="sm">
                <Button variant="default" onClick={() => setCreateModalOpen(false)}>
                  {t('common:cancel')}
                </Button>
                <Button onClick={handleCreate} loading={creating}>
                  {t('common:create')}
                </Button>
              </Group>
            </>
          )}
        </Stack>
      </Modal>

      {/* --- Revoke Confirm Modal --- */}
      <Modal opened={!!revokeTarget} onClose={() => setRevokeTarget(null)} title={t('apiKeys.revokeTitle')} size="sm">
        <Stack gap="md">
          <Text size="sm">
            <Trans
              i18nKey="settings:apiKeys.revokeConfirm"
              values={{ name: revokeTargetName }}
              components={{ strong: <strong /> }}
            />
          </Text>
          <Text size="sm">
            {t('apiKeys.revokeDesc')}
          </Text>
          <Group justify="flex-end" gap="sm">
            <Button variant="default" onClick={() => setRevokeTarget(null)}>
              {t('cancel')}
            </Button>
            <Button color="red" onClick={handleRevoke} loading={revoking}>
              {t('apiKeys.revoke')}
            </Button>
          </Group>
        </Stack>
      </Modal>
    </div>
  )
}

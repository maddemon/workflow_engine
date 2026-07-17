import { useState } from 'react';
import { Paper, Stack, Group, Text, Badge, Alert, Avatar, Title, Button } from '@mantine/core';
import { useTranslation } from 'react-i18next';
import { AlertCircle, User } from 'lucide-react';
import { useAuth } from '../hooks/AuthContext.tsx';
import { useRoles } from '../hooks/useRoles.ts';
import { RoleAssignModal } from '../components/admin/RoleAssignModal.tsx';

export function AdminUsersPage() {
  const { t } = useTranslation('admin');
  const { user } = useAuth();
  const { hasRole } = useRoles();
  const [roleModalOpen, setRoleModalOpen] = useState(false);
  const [refreshKey, setRefreshKey] = useState(0);

  if (!user) return null;

  return (
    <Stack p="md" gap="md">
      <Group>
        <Title order={3}>{t('usersPage.title')}</Title>
      </Group>

      <Alert icon={<AlertCircle size={16} />} color="blue" variant="light">
        {t('usersPage.userListUnavailable')}
      </Alert>

      <Paper withBorder p="md" radius="sm" key={refreshKey}>
        <Group gap="md">
          <Avatar size={48} radius="sm" color="brand-blue" variant="filled">
            {user.displayName?.[0]?.toUpperCase() ?? <User size={20} />}
          </Avatar>
          <Stack gap={4}>
            <Text fw={600}>{user.displayName}</Text>
            <Text size="sm" c="dimmed">{user.email}</Text>
            <Text size="xs" c="dimmed">@{user.userName}</Text>
          </Stack>
          <Group gap={4} ml="auto">
            {(user.roles ?? []).map((role) => (
              <Badge key={role} size="sm" variant="light" color="blue">{role}</Badge>
            ))}
          </Group>
          {hasRole('Admin') && (
            <Button size="xs" variant="outline" onClick={() => setRoleModalOpen(true)}>
              {t('usersPage.manageRoles')}
            </Button>
          )}
        </Group>
      </Paper>

      <RoleAssignModal
        opened={roleModalOpen}
        onClose={() => setRoleModalOpen(false)}
        userId={user.id}
        userName={user.displayName || user.userName}
        currentRoles={user.roles ?? []}
        onSaved={() => setRefreshKey((k) => k + 1)}
      />
    </Stack>
  );
}

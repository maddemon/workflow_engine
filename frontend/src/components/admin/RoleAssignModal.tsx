import { useState } from 'react';
import { Modal, Stack, Checkbox, Button, Group, Text } from '@mantine/core';
import { notifications } from '@mantine/notifications';
import { useTranslation } from 'react-i18next';
import { useRequest } from 'ahooks';
import * as api from '../../services/api.ts';

const ALL_ROLES = ['Admin', 'Editor', 'Viewer'];

interface RoleAssignModalProps {
  opened: boolean;
  onClose: () => void;
  userId: string;
  userName: string;
  currentRoles: string[];
  onSaved: () => void;
}

export function RoleAssignModal({ opened, onClose, userId, userName, currentRoles, onSaved }: RoleAssignModalProps) {
  const { t } = useTranslation('admin');
  const [selected, setSelected] = useState<string[]>(currentRoles);

  const handleToggle = (role: string) => {
    setSelected((prev) =>
      prev.includes(role) ? prev.filter((r) => r !== role) : [...prev, role]
    );
  };

  const { run: handleSave, loading: saving } = useRequest(
    async () => {
      const toAdd = selected.filter((r) => !currentRoles.includes(r));
      const toRemove = currentRoles.filter((r) => !selected.includes(r));
      for (const role of toAdd) {
        await api.assignRole(userId, role);
      }
      for (const role of toRemove) {
        await api.revokeRole(userId, role);
      }
    },
    {
      manual: true,
      onSuccess: () => {
        notifications.show({ title: t('roleModal.saved'), message: t('roleModal.savedMessage', { userName }), color: 'green' });
        onSaved();
        onClose();
      },
      onError: (err) => {
        notifications.show({
          title: t('roleModal.error'),
          message: err instanceof Error ? err.message : t('roleModal.updateFailed'),
          color: 'red',
        });
      },
    }
  );

  return (
    <Modal opened={opened} onClose={onClose} title={t('roleModal.title', { userName })} size="sm">
      <Stack gap="sm">
        <Text size="sm" c="dimmed">{t('roleModal.selectRoles')}</Text>
        {ALL_ROLES.map((role) => (
          <Checkbox
            key={role}
            label={role}
            checked={selected.includes(role)}
            onChange={() => handleToggle(role)}
          />
        ))}
        <Group justify="flex-end" mt="md">
          <Button variant="subtle" onClick={onClose}>{t('roleModal.cancel')}</Button>
          <Button onClick={handleSave} loading={saving}>{t('roleModal.save')}</Button>
        </Group>
      </Stack>
    </Modal>
  );
}

import { useState } from 'react';
import { Paper, Stack, Group, Table, Button, Modal, TextInput, Text, Title, ActionIcon, Tooltip } from '@mantine/core';
import { useForm } from '@mantine/form';
import { notifications } from '@mantine/notifications';
import { useTranslation } from 'react-i18next';
import { useRequest } from 'ahooks';
import { Plus, Edit, Trash2 } from 'lucide-react';
import * as api from '../services/api.ts';
import type { ProjectDto } from '../types/workflow.ts';

export function AdminProjectsPage() {
  const { t } = useTranslation('admin');
  const { data: projects = [], loading, refresh } = useRequest(api.getProjects);
  const [modalOpen, setModalOpen] = useState(false);
  const [deleteConfirm, setDeleteConfirm] = useState<string | null>(null);
  const [editing, setEditing] = useState<ProjectDto | null>(null);
  const form = useForm({
    initialValues: { name: '', description: '' },
    validate: { name: (v: string) => (!v.trim() ? t('projectsPage.nameRequired') : null) },
  });
  const [saving, setSaving] = useState(false);

  const openCreate = () => {
    setEditing(null);
    form.reset();
    setModalOpen(true);
  };

  const openEdit = (p: ProjectDto) => {
    setEditing(p);
    form.setValues({ name: p.name, description: p.description ?? '' });
    setModalOpen(true);
  };

  const handleSave = async () => {
    const validation = form.validate();
    if (validation.hasErrors) return;
    setSaving(true);
    try {
      if (editing) {
        await api.updateProject(editing.id, { name: form.values.name.trim(), description: form.values.description.trim() || null });
        notifications.show({ title: t('projectsPage.updated'), message: t('projectsPage.updatedMessage', { name: form.values.name }), color: 'green' });
      } else {
        await api.createProject({ name: form.values.name.trim(), description: form.values.description.trim() || null });
        notifications.show({ title: t('projectsPage.createdSuccess'), message: t('projectsPage.createdMessage', { name: form.values.name }), color: 'green' });
      }
      setModalOpen(false);
      refresh();
    } catch (err) {
      notifications.show({
        title: t('projectsPage.error'),
        message: err instanceof Error ? err.message : t('projectsPage.operationFailed'),
        color: 'red',
      });
    } finally {
      setSaving(false);
    }
  };

  const handleDelete = async (id: string) => {
    try {
      await api.deleteProject(id);
      notifications.show({ title: t('projectsPage.deleted'), message: t('projectsPage.deletedMessage'), color: 'green' });
      refresh();
    } catch (err) {
      notifications.show({
        title: t('projectsPage.error'),
        message: err instanceof Error ? err.message : t('projectsPage.deleteFailed'),
        color: 'red',
      });
    }
    setDeleteConfirm(null);
  };

  return (
    <Stack p="md" gap="md">
      <Group>
        <Title order={3}>{t('projectsPage.title')}</Title>
        <Button size="xs" leftSection={<Plus size={14} />} onClick={openCreate}>
          {t('projectsPage.newProject')}
        </Button>
      </Group>

      <Paper withBorder radius="sm">
        <Table striped highlightOnHover>
          <Table.Thead>
            <Table.Tr>
              <Table.Th>{t('projectsPage.name')}</Table.Th>
              <Table.Th>{t('projectsPage.description')}</Table.Th>
              <Table.Th>{t('projectsPage.created')}</Table.Th>
              <Table.Th style={{ width: 100, textAlign: 'right' }}>{t('projectsPage.actions')}</Table.Th>
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {projects.map((p) => (
              <Table.Tr key={p.id}>
                <Table.Td><Text fw={500}>{p.name}</Text></Table.Td>
                <Table.Td><Text size="sm" c="dimmed">{p.description || '—'}</Text></Table.Td>
                <Table.Td><Text size="sm">{new Date(p.createdAt).toLocaleDateString()}</Text></Table.Td>
                <Table.Td>
                  <Group gap={4} justify="flex-end">
                    <Tooltip label={t('projectsPage.edit')}>
                      <ActionIcon variant="subtle" color="gray" size="sm" aria-label={t('projectsPage.edit')} onClick={() => openEdit(p)}>
                        <Edit size={14} />
                      </ActionIcon>
                    </Tooltip>
                    <Tooltip label={t('projectsPage.delete')}>
                      <ActionIcon variant="subtle" color="red" size="sm" aria-label={t('projectsPage.delete')} onClick={() => setDeleteConfirm(p.id)}>
                        <Trash2 size={14} />
                      </ActionIcon>
                    </Tooltip>
                  </Group>
                </Table.Td>
              </Table.Tr>
            ))}
            {projects.length === 0 && !loading && (
              <Table.Tr>
                <Table.Td colSpan={4}><Text ta="center" c="dimmed" py="md">{t('projectsPage.noProjects')}</Text></Table.Td>
              </Table.Tr>
            )}
          </Table.Tbody>
        </Table>
      </Paper>

      {/* Create/Edit Modal */}
      <Modal
        opened={modalOpen}
        onClose={() => setModalOpen(false)}
        title={editing ? t('projectsPage.editProject') : t('projectsPage.newProject')}
        size="sm"
      >
        <Stack gap="sm">
          <TextInput
            label={t('projectsPage.name')}
            required
            {...form.getInputProps('name')}
            placeholder={t('projectsPage.projectName')}
          />
          <TextInput
            label={t('projectsPage.description')}
            {...form.getInputProps('description')}
            placeholder={t('projectsPage.optionalDescription')}
          />
          <Group justify="flex-end" mt="md">
            <Button variant="subtle" onClick={() => setModalOpen(false)}>{t('projectsPage.cancel')}</Button>
            <Button onClick={handleSave} loading={saving}>{editing ? t('projectsPage.update') : t('projectsPage.create')}</Button>
          </Group>
        </Stack>
      </Modal>

      {/* Delete Confirm Modal */}
      <Modal
        opened={!!deleteConfirm}
        onClose={() => setDeleteConfirm(null)}
        title={t('projectsPage.confirmDelete')}
        size="sm"
      >
        <Text size="sm" mb="md">{t('projectsPage.deleteWarning')}</Text>
        <Group justify="flex-end">
          <Button variant="subtle" onClick={() => setDeleteConfirm(null)}>{t('projectsPage.cancel')}</Button>
          <Button color="red" onClick={() => deleteConfirm && handleDelete(deleteConfirm)}>{t('projectsPage.delete')}</Button>
        </Group>
      </Modal>
    </Stack>
  );
}

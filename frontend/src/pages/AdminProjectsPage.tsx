import { useState } from 'react';
import { Paper, Stack, Group, Table, Button, Modal, TextInput, Text, Title, ActionIcon, Tooltip } from '@mantine/core';
import { useForm } from '@mantine/form';
import { notifications } from '@mantine/notifications';
import { useRequest } from 'ahooks';
import { Plus, Edit, Trash2 } from 'lucide-react';
import * as api from '../services/api.ts';
import type { ProjectDto } from '../types/workflow.ts';

export function AdminProjectsPage() {
  const { data: projects = [], loading, refresh } = useRequest(api.getProjects);
  const [modalOpen, setModalOpen] = useState(false);
  const [deleteConfirm, setDeleteConfirm] = useState<string | null>(null);
  const [editing, setEditing] = useState<ProjectDto | null>(null);
  const form = useForm({
    initialValues: { name: '', description: '' },
    validate: { name: (v: string) => (!v.trim() ? 'Project name is required' : null) },
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
        notifications.show({ title: 'Updated', message: `Project "${form.values.name}" updated.`, color: 'green' });
      } else {
        await api.createProject({ name: form.values.name.trim(), description: form.values.description.trim() || null });
        notifications.show({ title: 'Created', message: `Project "${form.values.name}" created.`, color: 'green' });
      }
      setModalOpen(false);
      refresh();
    } catch (err) {
      notifications.show({
        title: 'Error',
        message: err instanceof Error ? err.message : 'Operation failed',
        color: 'red',
      });
    } finally {
      setSaving(false);
    }
  };

  const handleDelete = async (id: string) => {
    try {
      await api.deleteProject(id);
      notifications.show({ title: 'Deleted', message: 'Project deleted.', color: 'green' });
      refresh();
    } catch (err) {
      notifications.show({
        title: 'Error',
        message: err instanceof Error ? err.message : 'Delete failed',
        color: 'red',
      });
    }
    setDeleteConfirm(null);
  };

  return (
    <Stack p="md" gap="md">
      <Group>
        <Title order={3}>Project Classification</Title>
        <Button size="xs" leftSection={<Plus size={14} />} onClick={openCreate}>
          New Project
        </Button>
      </Group>

      <Paper withBorder radius="sm">
        <Table striped highlightOnHover>
          <Table.Thead>
            <Table.Tr>
              <Table.Th>Name</Table.Th>
              <Table.Th>Description</Table.Th>
              <Table.Th>Created</Table.Th>
              <Table.Th style={{ width: 100, textAlign: 'right' }}>Actions</Table.Th>
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
                    <Tooltip label="Edit">
                      <ActionIcon variant="subtle" color="gray" size="sm" aria-label="Edit" onClick={() => openEdit(p)}>
                        <Edit size={14} />
                      </ActionIcon>
                    </Tooltip>
                    <Tooltip label="Delete">
                      <ActionIcon variant="subtle" color="red" size="sm" aria-label="Delete" onClick={() => setDeleteConfirm(p.id)}>
                        <Trash2 size={14} />
                      </ActionIcon>
                    </Tooltip>
                  </Group>
                </Table.Td>
              </Table.Tr>
            ))}
            {projects.length === 0 && !loading && (
              <Table.Tr>
                <Table.Td colSpan={4}><Text ta="center" c="dimmed" py="md">No projects yet.</Text></Table.Td>
              </Table.Tr>
            )}
          </Table.Tbody>
        </Table>
      </Paper>

      {/* Create/Edit Modal */}
      <Modal
        opened={modalOpen}
        onClose={() => setModalOpen(false)}
        title={editing ? 'Edit Project' : 'New Project'}
        size="sm"
      >
        <Stack gap="sm">
          <TextInput
            label="Name"
            required
            {...form.getInputProps('name')}
            placeholder="Project name"
          />
          <TextInput
            label="Description"
            {...form.getInputProps('description')}
            placeholder="Optional description"
          />
          <Group justify="flex-end" mt="md">
            <Button variant="subtle" onClick={() => setModalOpen(false)}>Cancel</Button>
            <Button onClick={handleSave} loading={saving}>{editing ? 'Update' : 'Create'}</Button>
          </Group>
        </Stack>
      </Modal>

      {/* Delete Confirm Modal */}
      <Modal
        opened={!!deleteConfirm}
        onClose={() => setDeleteConfirm(null)}
        title="Confirm Delete"
        size="sm"
      >
        <Text size="sm" mb="md">Are you sure you want to delete this project? Workflows in this project will become unclassified.</Text>
        <Group justify="flex-end">
          <Button variant="subtle" onClick={() => setDeleteConfirm(null)}>Cancel</Button>
          <Button color="red" onClick={() => deleteConfirm && handleDelete(deleteConfirm)}>Delete</Button>
        </Group>
      </Modal>
    </Stack>
  );
}

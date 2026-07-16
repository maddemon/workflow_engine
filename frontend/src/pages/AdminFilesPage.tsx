import { useState, useRef } from 'react';
import { Paper, Stack, Group, Table, Button, Select, Text, Title, ActionIcon, Tooltip, Alert, Modal } from '@mantine/core';
import { notifications } from '@mantine/notifications';
import { useRequest } from 'ahooks';
import { Upload, Download, Trash2, AlertCircle } from 'lucide-react';
import { getProjects, listFiles, uploadFile, downloadFile, deleteFile, formatFileSize } from '../services/api.ts';

export function AdminFilesPage() {
  const [selectedProjectId, setSelectedProjectId] = useState<string | null>(null);
  const [deleteConfirm, setDeleteConfirm] = useState<{ id: string; fileName: string } | null>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);
  const [dragOver, setDragOver] = useState(false);

  const { data: projects = [] } = useRequest(getProjects);

  const {
    data: files = [],
    loading,
    refresh,
  } = useRequest(
    () => (selectedProjectId ? listFiles(selectedProjectId) : Promise.resolve([])),
    { refreshDeps: [selectedProjectId] },
  );

  const handleUpload = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (!file || !selectedProjectId) return;
    try {
      await uploadFile(file, selectedProjectId);
      notifications.show({ title: 'Uploaded', message: `"${file.name}" uploaded.`, color: 'green' });
      refresh();
    } catch (err) {
      notifications.show({
        title: 'Upload failed',
        message: err instanceof Error ? err.message : 'Upload failed',
        color: 'red',
      });
    }
    if (fileInputRef.current) fileInputRef.current.value = '';
  };

  const handleDragOver = (e: React.DragEvent) => {
    e.preventDefault();
    e.stopPropagation();
    setDragOver(true);
  };

  const handleDragLeave = (e: React.DragEvent) => {
    e.preventDefault();
    e.stopPropagation();
    setDragOver(false);
  };

  const handleDrop = async (e: React.DragEvent) => {
    e.preventDefault();
    e.stopPropagation();
    setDragOver(false);

    const files = Array.from(e.dataTransfer.files);
    if (files.length === 0) return;

    if (!selectedProjectId) {
      notifications.show({ title: 'Error', message: 'Select a project first to upload files.', color: 'yellow' });
      return;
    }

    for (const file of files) {
      try {
        await uploadFile(file, selectedProjectId);
        notifications.show({ title: 'Uploaded', message: `"${file.name}" uploaded.`, color: 'green' });
      } catch (err) {
        notifications.show({
          title: 'Upload failed',
          message: err instanceof Error ? err.message : `Failed to upload "${file.name}"`,
          color: 'red',
        });
      }
    }
    refresh();
  };

  const handleDownload = async (id: string, fileName: string) => {
    try {
      const blob = await downloadFile(id);
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = fileName;
      a.click();
      URL.revokeObjectURL(url);
    } catch (err) {
      notifications.show({
        title: 'Download failed',
        message: err instanceof Error ? err.message : 'Download failed',
        color: 'red',
      });
    }
  };

  const handleDelete = async (id: string, fileName: string) => {
    try {
      await deleteFile(id);
      notifications.show({ title: 'Deleted', message: `"${fileName}" deleted.`, color: 'green' });
      refresh();
    } catch (err) {
      notifications.show({
        title: 'Delete failed',
        message: err instanceof Error ? err.message : 'Delete failed',
        color: 'red',
      });
    }
    setDeleteConfirm(null);
  };

  const confirmDelete = (id: string, fileName: string) => {
    setDeleteConfirm({ id, fileName });
  };

  const projectData = projects.map((p) => ({ value: p.id, label: p.name }));

  return (
    <Stack p="md" gap="md">
      <Title order={3}>File Management</Title>

      <Group>
        <Select
          size="sm"
          placeholder="Select a project"
          data={projectData}
          value={selectedProjectId}
          onChange={setSelectedProjectId}
          clearable
          w={300}
        />
        {selectedProjectId && (
          <>
            <input
              ref={fileInputRef}
              type="file"
              style={{ display: 'none' }}
              onChange={handleUpload}
            />
            <Button
              size="xs"
              leftSection={<Upload size={14} />}
              onClick={() => fileInputRef.current?.click()}
              aria-label="Upload file"
            >
              Upload File
            </Button>
          </>
        )}
      </Group>

      {!selectedProjectId && (
        <Alert icon={<AlertCircle size={16} />} color="blue" variant="light">
          Please select a project to view and manage its files.
        </Alert>
      )}

      {selectedProjectId && (
        <div
          onDragOver={handleDragOver}
          onDragLeave={handleDragLeave}
          onDrop={handleDrop}
          style={{
            border: dragOver ? '2px dashed var(--mantine-color-blue-5)' : '2px dashed transparent',
            borderRadius: 'var(--mantine-radius-sm)',
            transition: 'border-color 0.2s, background-color 0.2s',
            backgroundColor: dragOver ? 'var(--mantine-color-blue-0)' : 'transparent',
            padding: '4px',
            position: 'relative',
          }}
        >
          {dragOver && (
            <div style={{
              position: 'absolute',
              inset: 0,
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
              background: 'var(--mantine-color-body)',
              zIndex: 10,
              borderRadius: 'var(--mantine-radius-sm)',
              pointerEvents: 'none',
            }}>
              <Text size="sm" fw={500} c="blue">Drop files here to upload</Text>
            </div>
          )}
          <Paper withBorder radius="sm">
            <Table striped highlightOnHover>
              <Table.Thead>
                <Table.Tr>
                  <Table.Th>File Name</Table.Th>
                  <Table.Th>Type</Table.Th>
                  <Table.Th>Size</Table.Th>
                  <Table.Th>Uploaded</Table.Th>
                  <Table.Th style={{ width: 100, textAlign: 'right' }}>Actions</Table.Th>
                </Table.Tr>
              </Table.Thead>
              <Table.Tbody>
                {files.map((f) => (
                  <Table.Tr key={f.id}>
                    <Table.Td><Text fw={500} size="sm">{f.fileName}</Text></Table.Td>
                    <Table.Td><Text size="xs" c="dimmed">{f.contentType}</Text></Table.Td>
                    <Table.Td><Text size="sm">{formatFileSize(f.fileSize)}</Text></Table.Td>
                    <Table.Td><Text size="sm">{new Date(f.createdAt).toLocaleString()}</Text></Table.Td>
                    <Table.Td>
                      <Group gap={4} justify="flex-end">
                        <Tooltip label="Download">
                          <ActionIcon variant="subtle" color="blue" size="sm" aria-label="Download" onClick={() => handleDownload(f.id, f.fileName)}>
                            <Download size={14} />
                          </ActionIcon>
                        </Tooltip>
                        <Tooltip label="Delete">
                          <ActionIcon variant="subtle" color="red" size="sm" aria-label="Delete" onClick={() => confirmDelete(f.id, f.fileName)}>
                            <Trash2 size={14} />
                          </ActionIcon>
                        </Tooltip>
                      </Group>
                    </Table.Td>
                  </Table.Tr>
                ))}
                {files.length === 0 && !loading && (
                  <Table.Tr>
                    <Table.Td colSpan={5}><Text ta="center" c="dimmed" py="md">No files in this project.</Text></Table.Td>
                  </Table.Tr>
                )}
              </Table.Tbody>
            </Table>
          </Paper>
        </div>
      )}

      {/* Delete Confirm Modal */}
      <Modal
        opened={!!deleteConfirm}
        onClose={() => setDeleteConfirm(null)}
        title="Confirm Delete"
        size="sm"
      >
        <Text size="sm" mb="md">
          Are you sure you want to delete "{deleteConfirm?.fileName}"? This action cannot be undone.
        </Text>
        <Group justify="flex-end">
          <Button variant="subtle" onClick={() => setDeleteConfirm(null)}>Cancel</Button>
          <Button color="red" onClick={() => deleteConfirm && handleDelete(deleteConfirm.id, deleteConfirm.fileName)}>
            Delete
          </Button>
        </Group>
      </Modal>
    </Stack>
  );
}

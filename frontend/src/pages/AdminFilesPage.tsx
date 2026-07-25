import { useState, useRef } from 'react';
import { Paper, Stack, Group, Table, Button, Select, Text, Title, ActionIcon, Tooltip, Alert, Modal } from '@mantine/core';
import { notifications } from '@mantine/notifications';
import { useTranslation } from 'react-i18next';
import { useRequest } from 'ahooks';
import { Upload, Download, Trash2, AlertCircle } from 'lucide-react';
import { getProjects, listFiles, uploadFile, downloadFile, deleteFile, formatFileSize } from '../services/api.ts';
import { formatLocalDateTime } from '../utils/dateUtils.ts';
import styles from './AdminFilesPage.module.css';

export function AdminFilesPage() {
  const { t } = useTranslation('admin');
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
      notifications.show({ title: t('filesPage.uploadedSuccess'), message: t('filesPage.uploadedMessage', { name: file.name }), color: 'green' });
      refresh();
    } catch (err) {
      notifications.show({
        title: t('filesPage.uploadFailed'),
        message: err instanceof Error ? err.message : t('filesPage.uploadFailedMessage', { name: file.name }),
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
      notifications.show({ title: t('common:error'), message: t('filesPage.selectProjectFirst'), color: 'yellow' });
      return;
    }

    for (const file of files) {
      try {
        await uploadFile(file, selectedProjectId);
        notifications.show({ title: t('filesPage.uploadedSuccess'), message: t('filesPage.uploadedMessage', { name: file.name }), color: 'green' });
      } catch (err) {
        notifications.show({
          title: t('filesPage.uploadFailed'),
          message: err instanceof Error ? err.message : t('filesPage.uploadFailedMessage', { name: file.name }),
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
        title: t('filesPage.downloadFailed'),
        message: err instanceof Error ? err.message : t('filesPage.downloadFailed'),
        color: 'red',
      });
    }
  };

  const handleDelete = async (id: string, fileName: string) => {
    try {
      await deleteFile(id);
      notifications.show({ title: t('filesPage.deletedSuccess'), message: t('filesPage.deletedMessage', { name: fileName }), color: 'green' });
      refresh();
    } catch (err) {
      notifications.show({
        title: t('filesPage.deleteFailed'),
        message: err instanceof Error ? err.message : t('filesPage.deleteFailed'),
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
      <Title order={3}>{t('filesPage.title')}</Title>

      <Group>
        <Select
          size="sm"
          placeholder={t('filesPage.selectProject')}
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
              className={styles.hiddenInput}
              onChange={handleUpload}
            />
            <Button
              size="xs"
              leftSection={<Upload size={14} />}
              onClick={() => fileInputRef.current?.click()}
              aria-label={t('filesPage.uploadFile')}
            >
              {t('filesPage.uploadFile')}
            </Button>
          </>
        )}
      </Group>

      {!selectedProjectId && (
        <Alert icon={<AlertCircle size={16} />} color="blue" variant="light">
          {t('filesPage.selectProjectHint')}
        </Alert>
      )}

      {selectedProjectId && (
        <div
          onDragOver={handleDragOver}
          onDragLeave={handleDragLeave}
          onDrop={handleDrop}
          className={`${styles.dropZone} ${dragOver ? styles.dropZoneActive : styles.dropZoneIdle}`}
        >
          {dragOver && (
            <div className={styles.dropOverlay}>
              <Text size="sm" fw={500} c="blue">{t('filesPage.dropFilesHere')}</Text>
            </div>
          )}
          <Paper withBorder radius="sm">
            <Table striped highlightOnHover>
              <Table.Thead>
                <Table.Tr>
                  <Table.Th>{t('filesPage.fileName')}</Table.Th>
                  <Table.Th>{t('filesPage.type')}</Table.Th>
                  <Table.Th>{t('filesPage.size')}</Table.Th>
                  <Table.Th>{t('filesPage.uploaded')}</Table.Th>
                  <Table.Th style={{ width: 100, textAlign: 'right' }}>{t('filesPage.actions')}</Table.Th>
                </Table.Tr>
              </Table.Thead>
              <Table.Tbody>
                {files.map((f) => (
                  <Table.Tr key={f.id}>
                    <Table.Td><Text fw={500} size="sm">{f.fileName}</Text></Table.Td>
                    <Table.Td><Text size="xs" c="dimmed">{f.contentType}</Text></Table.Td>
                    <Table.Td><Text size="sm">{formatFileSize(f.fileSize)}</Text></Table.Td>
                    <Table.Td><Text size="sm">{formatLocalDateTime(f.createdAt)}</Text></Table.Td>
                    <Table.Td>
                      <Group gap={4} justify="flex-end">
                        <Tooltip label={t('filesPage.download')}>
                          <ActionIcon variant="subtle" color="blue" size="sm" aria-label={t('filesPage.download')} onClick={() => handleDownload(f.id, f.fileName)}>
                            <Download size={14} />
                          </ActionIcon>
                        </Tooltip>
                        <Tooltip label={t('filesPage.delete')}>
                          <ActionIcon variant="subtle" color="red" size="sm" aria-label={t('filesPage.delete')} onClick={() => confirmDelete(f.id, f.fileName)}>
                            <Trash2 size={14} />
                          </ActionIcon>
                        </Tooltip>
                      </Group>
                    </Table.Td>
                  </Table.Tr>
                ))}
                {files.length === 0 && !loading && (
                  <Table.Tr>
                    <Table.Td colSpan={5}><Text ta="center" c="dimmed" py="md">{t('filesPage.noFiles')}</Text></Table.Td>
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
        title={t('filesPage.confirmDelete')}
        size="sm"
      >
        <Text size="sm" mb="md">
          {t('filesPage.deleteWarning', { fileName: deleteConfirm?.fileName })}
        </Text>
        <Group justify="flex-end">
          <Button variant="subtle" onClick={() => setDeleteConfirm(null)}>{t('filesPage.cancel')}</Button>
          <Button color="red" onClick={() => deleteConfirm && handleDelete(deleteConfirm.id, deleteConfirm.fileName)}>
            {t('filesPage.delete')}
          </Button>
        </Group>
      </Modal>
    </Stack>
  );
}

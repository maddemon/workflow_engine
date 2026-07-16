import { useState, useMemo } from 'react';
import {
  Stack,
  Text,
  Button,
  Loader,
  Center,
  Alert,
  Group,
  Table,
  Checkbox,
  Badge,
  ActionIcon,
  Menu,
  Modal,
  FileInput,
  Box,
  Code,
  List,
  Tooltip,
} from '@mantine/core';
import { notifications } from '@mantine/notifications';
import {
  Plus,
  AlertCircle,
  RefreshCw,
  Download,
  Upload,
  FileJson,
  Trash,
  Edit,
  History,
  MoreVertical,
  Workflow as WorkflowIcon,
  Clock,
  CalendarClock,
  Globe,
  Folder,
} from 'lucide-react';
import { useRequest } from 'ahooks';
import { useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import {
  getWorkflows,
  getProjects,
  exportWorkflow,
  exportWorkflowsBatch,
  importWorkflow,
  importWorkflowsBatch,
} from '../../services/api.ts';
import { useWorkflowStore } from '../../stores/workflowStore.ts';
import { useAuth } from '../../hooks/AuthContext.tsx';
import { ProjectFilter } from './ProjectFilter.tsx';
import type {
  WorkflowSummary,
  ImportResult,
  BatchImportResult,
} from '../../types/workflow.ts';

function downloadJson(content: string, filename: string) {
  const blob = new Blob([content], { type: 'application/json' });
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = filename;
  document.body.appendChild(a);
  a.click();
  document.body.removeChild(a);
  URL.revokeObjectURL(url);
}

function formatDateTime(iso: string | null | undefined): string {
  if (!iso) return '—';
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return '—';
  return d.toLocaleString(undefined, {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
  });
}

function readFileAsText(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(reader.result as string);
    reader.onerror = () => reject(new Error('Failed to read file'));
    reader.readAsText(file);
  });
}

export function WorkflowListPage() {
  const { t } = useTranslation(['workflow', 'common']);
  const [selectedIds, setSelectedIds] = useState<Set<string>>(new Set());
  const [exporting, setExporting] = useState(false);
  const [importOpen, setImportOpen] = useState(false);
  const [importFile, setImportFile] = useState<File | null>(null);
  const [importing, setImporting] = useState(false);
  const [importResult, setImportResult] = useState<ImportResult | BatchImportResult | null>(null);
  const [importMode, setImportMode] = useState<'single' | 'batch'>('single');
  const navigate = useNavigate();
  const newWorkflow = useWorkflowStore((s) => s.newWorkflow);
  const deleteWorkflow = useWorkflowStore((s) => s.deleteWorkflow);
  const { user } = useAuth();
  const [fileInputKey, setFileInputKey] = useState(0);

  const { data: workflows = [], loading, error, refresh: refreshWorkflows } = useRequest(getWorkflows);
  const { data: projects = [] } = useRequest(getProjects, {
    pollingInterval: 60000,
  });
  const projectMap = useMemo(() => {
    const map = new Map<string, string>();
    for (const p of projects) map.set(p.id, p.name);
    return map;
  }, [projects]);
  const [projectFilter, setProjectFilter] = useState<string | null>(null);

  const filteredWorkflows = projectFilter === '__none__'
    ? workflows.filter((w) => !w.projectId)
    : projectFilter
      ? workflows.filter((w) => w.projectId === projectFilter)
      : workflows;

  const toggleSelect = (id: string) => {
    setSelectedIds((prev) => {
      const next = new Set(prev);
      if (next.has(id)) {
        next.delete(id);
      } else {
        next.add(id);
      }
      return next;
    });
  };

  const toggleSelectAll = () => {
    const allFilteredIds = filteredWorkflows.map((w) => w.id);
    if (selectedIds.size === allFilteredIds.length && allFilteredIds.length > 0) {
      setSelectedIds(new Set());
    } else {
      setSelectedIds(new Set(allFilteredIds));
    }
  };

  const handleNew = () => {
    newWorkflow();
    // If a project is selected in the filter, propagate it to the new workflow
    if (projectFilter && projectFilter !== '__none__') {
      useWorkflowStore.getState().setProjectId?.(projectFilter);
    }
    navigate('/workflow/new');
  };

  const handleDelete = async (id: string, name: string) => {
    if (!confirm(t('list.confirmDelete', { name }))) return;
    try {
      await deleteWorkflow(id);
      setSelectedIds((prev) => {
        const next = new Set(prev);
        next.delete(id);
        return next;
      });
      await refreshWorkflows();
      notifications.show({ title: t('list.deleted'), message: t('list.deletedMessage', { name }), color: 'green' });
    } catch (err) {
      notifications.show({
        title: t('list.deleteFailed'),
        message: err instanceof Error ? err.message : t('list.deleteFailedMessage'),
        color: 'red',
      });
    }
  };

  const handleExport = async () => {
    if (selectedIds.size === 0) return;
    setExporting(true);
    try {
      const ids = Array.from(selectedIds);
      if (ids.length === 1) {
        const result = await exportWorkflow(ids[0]);
        downloadJson(JSON.stringify(result, null, 2), `${result.name}-v${result.version}.json`);
      } else {
        const results = await exportWorkflowsBatch(ids);
        downloadJson(
          JSON.stringify(results, null, 2),
          `workflows-export-${new Date().toISOString().slice(0, 10)}.json`,
        );
      }
      notifications.show({
        title: t('list.exportComplete'),
        message: t('list.exportCompleteMessage', { count: ids.length }),
        color: 'green',
      });
    } catch (err) {
      notifications.show({
        title: t('list.exportFailed'),
        message: err instanceof Error ? err.message : t('list.exportFailedMessage'),
        color: 'red',
      });
    } finally {
      setExporting(false);
    }
  };

  const handleExportSingle = async (wf: WorkflowSummary) => {
    try {
      const result = await exportWorkflow(wf.id);
      downloadJson(JSON.stringify(result, null, 2), `${result.name}-v${result.version}.json`);
      notifications.show({ title: t('list.exported'), message: t('list.exportedMessage', { name: wf.name }), color: 'green' });
    } catch (err) {
      notifications.show({
        title: t('list.exportFailed'),
        message: err instanceof Error ? err.message : t('list.exportFailedMessage'),
        color: 'red',
      });
    }
  };

  const openImport = () => {
    setImportResult(null);
    setImportFile(null);
    setImportOpen(true);
    setFileInputKey((k) => k + 1);
  };

  const handleImport = async () => {
    if (!importFile) return;
    setImporting(true);
    setImportResult(null);
    try {
      const json = await readFileAsText(importFile);
      const trimmed = json.trim();
      try {
        JSON.parse(trimmed);
      } catch {
        notifications.show({
          title: t('list.invalidFile'),
          message: t('list.invalidFileMessage'),
          color: 'red',
        });
        return;
      }
      const importedBy = user?.userName ?? 'unknown';
      const isArray = trimmed.startsWith('[');
      setImportMode(isArray ? 'batch' : 'single');
      if (isArray) {
        const result = await importWorkflowsBatch({ json, importedBy });
        setImportResult(result);
      } else {
        const result = await importWorkflow({ json, importedBy });
        setImportResult(result);
      }
      setImportFile(null);
      await refreshWorkflows();
    } catch (err: unknown) {
      const response = (err as { response?: { data?: unknown } })?.response;
      const data = response?.data;
      if (data && typeof data === 'object' && ('success' in data || 'successCount' in data)) {
        setImportResult(data as ImportResult | BatchImportResult);
        await refreshWorkflows();
        return;
      }
      notifications.show({
        title: t('list.importFailed'),
        message: err instanceof Error ? err.message : t('list.importFailedMessage'),
        color: 'red',
      });
    } finally {
      setImporting(false);
    }
  };

  if (loading) {
    return (
      <Center h="100%" style={{ background: 'var(--bg-page)' }}>
        <Loader size="md" />
      </Center>
    );
  }

  if (error) {
    return (
      <Center h="100%" p="md" style={{ background: 'var(--bg-page)' }}>
        <Alert icon={<AlertCircle size={16} />} title={t('error', { ns: 'common' })} color="red" w={400}>
          {error.message ?? t('list.loadErrorMessage')}
        </Alert>
      </Center>
    );
  }

  return (
    <Stack gap="md" p="md" h="100%" style={{ overflow: 'auto', background: 'var(--bg-page)' }}>
      <Group justify="space-between" align="center">
        <Group gap="xs">
          <WorkflowIcon size={20} />
          <Text fw={700} size="lg">{t('list.title')}</Text>
          <ProjectFilter value={projectFilter} onChange={setProjectFilter} />
          {selectedIds.size > 0 && (
            <Badge variant="light" color="blue">{t('list.selectedCount', { count: selectedIds.size })}</Badge>
          )}
        </Group>
        <Group gap="xs">
          <Tooltip label={t('refresh', { ns: 'common' })}>
            <Button variant="subtle" size="sm" onClick={refreshWorkflows} disabled={loading}>
              <RefreshCw size={16} />
            </Button>
          </Tooltip>
          <Button
            variant="subtle"
            size="sm"
            leftSection={<Upload size={14} />}
            onClick={openImport}
          >
            {t('list.import')}
          </Button>
          <Button
            variant="subtle"
            size="sm"
            leftSection={<Download size={14} />}
            onClick={handleExport}
            loading={exporting}
            disabled={selectedIds.size === 0}
          >
            {selectedIds.size > 0 ? t('list.export') + ` (${selectedIds.size})` : t('list.export')}
          </Button>
          <Button size="sm" leftSection={<Plus size={14} />} onClick={handleNew}>
            {t('list.new')}
          </Button>
        </Group>
      </Group>

      {filteredWorkflows.length === 0 ? (
        <Center h="60%">
          <Stack align="center" gap="md">
            <ActionIcon size={64} radius="xl" variant="light" color="gray" disabled>
              <WorkflowIcon size={32} />
            </ActionIcon>
            <Text c="dimmed" size="sm">{t('list.noWorkflows')}</Text>
            <Group gap="xs">
              <Button variant="subtle" leftSection={<Upload size={14} />} onClick={openImport}>
                {t('list.import')}
              </Button>
              <Button leftSection={<Plus size={14} />} onClick={handleNew}>
                {t('list.new')}
              </Button>
            </Group>
          </Stack>
        </Center>
      ) : (
        <Table striped highlightOnHover>
          <Table.Thead>
            <Table.Tr>
              <Table.Th style={{ width: 40 }}>
                <Checkbox
                  checked={selectedIds.size === filteredWorkflows.length && filteredWorkflows.length > 0}
                  onChange={toggleSelectAll}
                />
              </Table.Th>
              <Table.Th style={{ width: 90 }}>{t('list.status')}</Table.Th>
              <Table.Th>{t('list.name')}</Table.Th>
              <Table.Th style={{ width: 110 }}>{t('list.project')}</Table.Th>
              <Table.Th style={{ width: 160 }}>{t('list.lastRun')}</Table.Th>
              <Table.Th style={{ width: 170 }}>{t('list.triggers')}</Table.Th>
              <Table.Th style={{ width: 150 }}>{t('list.updated')}</Table.Th>
              <Table.Th style={{ width: 120, textAlign: 'right' }}>{t('list.actions')}</Table.Th>
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {filteredWorkflows.map((wf) => (
              <Table.Tr key={wf.id}>
                <Table.Td>
                  <Checkbox
                    checked={selectedIds.has(wf.id)}
                    onChange={() => toggleSelect(wf.id)}
                  />
                </Table.Td>
                <Table.Td>
                  <Badge
                    size="sm"
                    variant="light"
                    color={wf.isActive ? 'green' : 'gray'}
                  >
                    {wf.isActive ? t('list.active') : t('list.inactive')}
                  </Badge>
                  {wf.source === 'ai' && !wf.isActive && wf.draftStatus === 'pending' && (
                    <Badge size="sm" variant="light" color="blue" ml={4}>{t('list.aiDraftPending')}</Badge>
                  )}
                </Table.Td>
                <Table.Td>
                  <Group gap="xs" wrap="nowrap">
                    <Text
                      fw={500}
                      style={{ cursor: 'pointer' }}
                      onClick={() => navigate(`/workflow/${wf.id}`)}
                    >
                      {wf.name}
                    </Text>
                    <Text size="xs" c="dimmed">v{wf.version}</Text>
                  </Group>
                </Table.Td>
                <Table.Td>
                  {wf.projectId ? (
                    <Tooltip label={projectMap.get(wf.projectId) ? t('list.projectTooltip', { name: projectMap.get(wf.projectId) }) : t('list.unknownProject')}>
                      <Badge size="sm" variant="light" color="blue" leftSection={<Folder size={10} />}>
                        {projectMap.get(wf.projectId) || t('list.project')}
                      </Badge>
                    </Tooltip>
                  ) : (
                    <Tooltip label={t('list.globalWorkflowTooltip')}>
                      <Badge size="sm" variant="light" color="teal" leftSection={<Globe size={10} />}>
                        {t('list.global')}
                      </Badge>
                    </Tooltip>
                  )}
                </Table.Td>
                <Table.Td>
                  <Group gap="xs" wrap="nowrap">
                    <Clock size={12} color="var(--mantine-color-dimmed-text)" />
                    <Text size="xs" c={wf.lastExecutionAt ? 'dimmed' : 'disabled'}>
                      {formatDateTime(wf.lastExecutionAt)}
                    </Text>
                  </Group>
                </Table.Td>
                <Table.Td>
                  {wf.triggerCount > 0 ? (
                    <Group gap="xs" wrap="nowrap">
                      <Badge size="sm" variant="outline" color="indigo">{wf.triggerCount}</Badge>
                      {wf.nextTriggerAt && (
                        <Tooltip label={t('list.nextTrigger', { time: formatDateTime(wf.nextTriggerAt) })}>
                          <Group gap={4} wrap="nowrap">
                            <CalendarClock size={12} color="var(--mantine-color-indigo-text)" />
                            <Text size="xs" c="indigo">{formatDateTime(wf.nextTriggerAt)}</Text>
                          </Group>
                        </Tooltip>
                      )}
                    </Group>
                  ) : (
                    <Text size="xs" c="disabled">—</Text>
                  )}
                </Table.Td>
                <Table.Td>
                  <Text size="xs" c="dimmed">{formatDateTime(wf.updatedAt ?? wf.createdAt)}</Text>
                </Table.Td>
                <Table.Td style={{ textAlign: 'right' }}>
                  <Group gap={4} justify="flex-end" wrap="nowrap">
                    <Tooltip label={t('list.openEditor')}>
                      <ActionIcon
                        variant="subtle"
                        size="sm"
                        onClick={() => navigate(`/workflow/${wf.id}`)}
                      >
                        <Edit size={14} />
                      </ActionIcon>
                    </Tooltip>
                    <Tooltip label={t('list.executionHistory')}>
                      <ActionIcon
                        variant="subtle"
                        size="sm"
                        onClick={() => navigate(`/workflow/${wf.id}/history`)}
                      >
                        <History size={14} />
                      </ActionIcon>
                    </Tooltip>
                    <Menu position="bottom-end" withinPortal>
                      <Menu.Target>
                        <ActionIcon variant="subtle" size="sm">
                          <MoreVertical size={14} />
                        </ActionIcon>
                      </Menu.Target>
                      <Menu.Dropdown>
                        <Menu.Item
                          leftSection={<Download size={12} />}
                          onClick={() => handleExportSingle(wf)}
                        >
                          {t('list.export')}
                        </Menu.Item>
                        <Menu.Divider />
                        <Menu.Item
                          color="red"
                          leftSection={<Trash size={12} />}
                          onClick={() => handleDelete(wf.id, wf.name)}
                        >
                          {t('delete', { ns: 'common' })}
                        </Menu.Item>
                      </Menu.Dropdown>
                    </Menu>
                  </Group>
                </Table.Td>
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
      )}

      {/* Import Modal */}
      <Modal
        opened={importOpen}
        onClose={() => setImportOpen(false)}
        title={t('import.title')}
        centered
        size="lg"
      >
        <Stack gap="md">
          <FileInput
            label={t('import.jsonFile')}
            placeholder={t('import.selectWorkflowJson')}
            value={importFile}
            onChange={setImportFile}
            accept="application/json,.json"
            leftSection={<FileJson size={16} />}
            clearable
            key={fileInputKey}
          />
          <Text size="xs" c="dimmed">
            {t('import.hint')}
          </Text>
          <Group justify="flex-end">
            <Button
              variant="subtle"
              color="gray"
              onClick={() => setImportOpen(false)}
              disabled={importing}
            >
              {t('close', { ns: 'common' })}
            </Button>
            <Button
              leftSection={<Upload size={14} />}
              onClick={handleImport}
              loading={importing}
              disabled={!importFile}
            >
              {t('list.import')}
            </Button>
          </Group>

          {importResult && (
            <Box>
              <Text fw={600} size="sm" mb="xs">{t('import.result')}</Text>
              {importMode === 'single' ? (
                <SingleImportResultView result={importResult as ImportResult} />
              ) : (
                <BatchImportResultView result={importResult as BatchImportResult} />
              )}
            </Box>
          )}
        </Stack>
      </Modal>
    </Stack>
  );
}

function SingleImportResultView({ result }: { result: ImportResult }) {
  const { t } = useTranslation(['workflow', 'common']);
  return (
    <Stack gap="xs">
      <Group gap="xs">
        <Text size="sm" c={result.success ? 'green' : 'red'} fw={600}>
          {result.success ? t('import.success') : t('import.failed')}
        </Text>
        {result.workflowId && (
          <Text size="xs" c="dimmed">{t('import.id')}: <Code>{result.workflowId}</Code></Text>
        )}
        {result.workflowName && (
          <Text size="xs" c="dimmed">{t('list.name')}: {result.workflowName}</Text>
        )}
      </Group>
      {result.errors.length > 0 && (
        <Stack gap={2}>
          {result.errors.map((err, idx) => (
            <Text key={`${err.errorType}-${err.nodeId}-${idx}`} size="xs" c="red">
              [{err.errorType}] {err.message}
              {err.nodeId && ` (${t('import.errorNode', { nodeId: err.nodeId })})`}
            </Text>
          ))}
        </Stack>
      )}
    </Stack>
  );
}

function BatchImportResultView({ result }: { result: BatchImportResult }) {
  const { t } = useTranslation(['workflow', 'common']);
  return (
    <Stack gap="xs">
      <Group gap="md">
        <Text size="sm" c="green" fw={600}>{t('import.successCount', { count: result.successCount })}</Text>
        <Text size="sm" c="red" fw={600}>{t('import.failureCount', { count: result.failureCount })}</Text>
      </Group>
      {result.results.length > 0 && (
        <List size="xs" withPadding>
          {result.results.map((r, idx) => {
            const name = r.workflowName ?? t('import.workflowNumber', { number: idx + 1 });
            return (
              <List.Item key={idx} c={r.success ? 'green' : 'red'}>
                {r.success
                  ? t('import.importedItem', { name })
                  : t('import.importFailedItem', { name, message: r.errors.map((e) => e.message).join('; ') })}
              </List.Item>
            );
          })}
        </List>
      )}
    </Stack>
  );
}

import { useState, useEffect, useCallback, useRef } from 'react';
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
import { useNavigate } from 'react-router-dom';
import {
  getWorkflows,
  exportWorkflow,
  exportWorkflowsBatch,
  importWorkflow,
  importWorkflowsBatch,
} from '../../services/api.ts';
import { useWorkflowStore } from '../../stores/workflowStore.ts';
import { useAuth } from '../../hooks/AuthContext.tsx';
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
  const [workflows, setWorkflows] = useState<WorkflowSummary[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
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
  const fileInputResetRef = useRef<number>(0);

  const loadWorkflows = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const list = await getWorkflows();
      setWorkflows(list);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load workflows');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    loadWorkflows();
  }, [loadWorkflows]);

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
    if (selectedIds.size === workflows.length && workflows.length > 0) {
      setSelectedIds(new Set());
    } else {
      setSelectedIds(new Set(workflows.map((w) => w.id)));
    }
  };

  const handleNew = () => {
    newWorkflow();
    navigate('/workflow/new');
  };

  const handleDelete = async (id: string, name: string) => {
    if (!confirm(`Delete workflow "${name}"?`)) return;
    try {
      await deleteWorkflow(id);
      setWorkflows((prev) => prev.filter((w) => w.id !== id));
      setSelectedIds((prev) => {
        const next = new Set(prev);
        next.delete(id);
        return next;
      });
      notifications.show({ title: 'Deleted', message: `Workflow "${name}" deleted.`, color: 'green' });
    } catch (err) {
      notifications.show({
        title: 'Delete failed',
        message: err instanceof Error ? err.message : 'Failed to delete workflow',
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
        title: 'Export complete',
        message: `${ids.length} workflow(s) exported.`,
        color: 'green',
      });
    } catch (err) {
      notifications.show({
        title: 'Export failed',
        message: err instanceof Error ? err.message : 'Failed to export workflows',
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
      notifications.show({ title: 'Exported', message: `Workflow "${wf.name}" exported.`, color: 'green' });
    } catch (err) {
      notifications.show({
        title: 'Export failed',
        message: err instanceof Error ? err.message : 'Failed to export workflow',
        color: 'red',
      });
    }
  };

  const openImport = () => {
    setImportResult(null);
    setImportFile(null);
    setImportOpen(true);
    fileInputResetRef.current += 1;
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
          title: 'Invalid file',
          message: 'Selected file is not valid JSON.',
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
      await loadWorkflows();
    } catch (err: unknown) {
      const response = (err as { response?: { data?: unknown } })?.response;
      const data = response?.data;
      if (data && typeof data === 'object' && ('success' in data || 'successCount' in data)) {
        setImportResult(data as ImportResult | BatchImportResult);
        await loadWorkflows();
        return;
      }
      notifications.show({
        title: 'Import failed',
        message: err instanceof Error ? err.message : 'Failed to import workflow',
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
        <Alert icon={<AlertCircle size={16} />} title="Error" color="red" w={400}>
          {error}
        </Alert>
      </Center>
    );
  }

  return (
    <Stack gap="md" p="md" h="100%" style={{ overflow: 'auto', background: 'var(--bg-page)' }}>
      <Group justify="space-between" align="center">
        <Group gap="xs">
          <WorkflowIcon size={20} />
          <Text fw={700} size="lg">Workflows</Text>
          {selectedIds.size > 0 && (
            <Badge variant="light" color="blue">{selectedIds.size} selected</Badge>
          )}
        </Group>
        <Group gap="xs">
          <Tooltip label="Refresh">
            <Button variant="subtle" size="sm" onClick={loadWorkflows} disabled={loading}>
              <RefreshCw size={16} />
            </Button>
          </Tooltip>
          <Button
            variant="subtle"
            size="sm"
            leftSection={<Upload size={14} />}
            onClick={openImport}
          >
            Import
          </Button>
          <Button
            variant="subtle"
            size="sm"
            leftSection={<Download size={14} />}
            onClick={handleExport}
            loading={exporting}
            disabled={selectedIds.size === 0}
          >
            Export{selectedIds.size > 0 ? ` (${selectedIds.size})` : ''}
          </Button>
          <Button size="sm" leftSection={<Plus size={14} />} onClick={handleNew}>
            New Workflow
          </Button>
        </Group>
      </Group>

      {workflows.length === 0 ? (
        <Center h="60%">
          <Stack align="center" gap="md">
            <ActionIcon size={64} radius="xl" variant="light" color="gray" disabled>
              <WorkflowIcon size={32} />
            </ActionIcon>
            <Text c="dimmed" size="sm">No workflows yet.</Text>
            <Group gap="xs">
              <Button variant="subtle" leftSection={<Upload size={14} />} onClick={openImport}>
                Import
              </Button>
              <Button leftSection={<Plus size={14} />} onClick={handleNew}>
                New Workflow
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
                  checked={selectedIds.size === workflows.length && workflows.length > 0}
                  onChange={toggleSelectAll}
                />
              </Table.Th>
              <Table.Th style={{ width: 90 }}>Status</Table.Th>
              <Table.Th>Name</Table.Th>
              <Table.Th style={{ width: 110 }}>Project</Table.Th>
              <Table.Th style={{ width: 160 }}>Last Run</Table.Th>
              <Table.Th style={{ width: 170 }}>Triggers</Table.Th>
              <Table.Th style={{ width: 150 }}>Updated</Table.Th>
              <Table.Th style={{ width: 120, textAlign: 'right' }}>Actions</Table.Th>
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {workflows.map((wf) => (
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
                    {wf.isActive ? 'Active' : 'Inactive'}
                  </Badge>
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
                    <Tooltip label={wf.projectId}>
                      <Badge size="sm" variant="light" color="blue" leftSection={<Folder size={10} />}>
                        Project
                      </Badge>
                    </Tooltip>
                  ) : (
                    <Tooltip label="Global workflow (no project)">
                      <Badge size="sm" variant="light" color="teal" leftSection={<Globe size={10} />}>
                        Global
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
                        <Tooltip label={`Next: ${formatDateTime(wf.nextTriggerAt)}`}>
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
                    <Tooltip label="Open editor">
                      <ActionIcon
                        variant="subtle"
                        size="sm"
                        onClick={() => navigate(`/workflow/${wf.id}`)}
                      >
                        <Edit size={14} />
                      </ActionIcon>
                    </Tooltip>
                    <Tooltip label="Execution history">
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
                          Export
                        </Menu.Item>
                        <Menu.Divider />
                        <Menu.Item
                          color="red"
                          leftSection={<Trash size={12} />}
                          onClick={() => handleDelete(wf.id, wf.name)}
                        >
                          Delete
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
        title="Import Workflows"
        centered
        size="lg"
      >
        <Stack gap="md">
          <FileInput
            label="JSON file"
            placeholder="Select workflow JSON file (single object or array)"
            value={importFile}
            onChange={setImportFile}
            accept="application/json,.json"
            leftSection={<FileJson size={16} />}
            clearable
            key={fileInputResetRef.current}
          />
          <Text size="xs" c="dimmed">
            Single object → one workflow; array → batch import. Imported workflows will use your
            default project (or the original project if accessible).
          </Text>
          <Group justify="flex-end">
            <Button
              variant="subtle"
              color="gray"
              onClick={() => setImportOpen(false)}
              disabled={importing}
            >
              Close
            </Button>
            <Button
              leftSection={<Upload size={14} />}
              onClick={handleImport}
              loading={importing}
              disabled={!importFile}
            >
              Import
            </Button>
          </Group>

          {importResult && (
            <Box>
              <Text fw={600} size="sm" mb="xs">Result</Text>
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
  return (
    <Stack gap="xs">
      <Group gap="xs">
        <Text size="sm" c={result.success ? 'green' : 'red'} fw={600}>
          {result.success ? 'Success' : 'Failed'}
        </Text>
        {result.workflowId && (
          <Text size="xs" c="dimmed">ID: <Code>{result.workflowId}</Code></Text>
        )}
        {result.workflowName && (
          <Text size="xs" c="dimmed">Name: {result.workflowName}</Text>
        )}
      </Group>
      {result.errors.length > 0 && (
        <Stack gap={2}>
          {result.errors.map((err, idx) => (
            <Text key={idx} size="xs" c="red">
              [{err.errorType}] {err.message}
              {err.nodeId && ` (node: ${err.nodeId})`}
            </Text>
          ))}
        </Stack>
      )}
    </Stack>
  );
}

function BatchImportResultView({ result }: { result: BatchImportResult }) {
  return (
    <Stack gap="xs">
      <Group gap="md">
        <Text size="sm" c="green" fw={600}>Success: {result.successCount}</Text>
        <Text size="sm" c="red" fw={600}>Failed: {result.failureCount}</Text>
      </Group>
      {result.results.length > 0 && (
        <List size="xs" withPadding>
          {result.results.map((r, idx) => (
            <List.Item key={idx} c={r.success ? 'green' : 'red'}>
              {r.success
                ? `${r.workflowName ?? `Workflow ${idx + 1}`} imported`
                : `${r.workflowName ?? `Workflow ${idx + 1}`} failed: ${r.errors.map((e) => e.message).join('; ')}`}
            </List.Item>
          ))}
        </List>
      )}
    </Stack>
  );
}

import { useState, useEffect, useMemo } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import {
  Stack,
  Text,
  Group,
  ActionIcon,
  Table,
  Badge,
  Loader,
  Select,
  Paper,
  Divider,
  Pagination,
} from '@mantine/core';
import { ArrowLeft, RefreshCw, Check, X, Clock, Loader as LoaderIcon, Eye, ChevronDown, ChevronRight } from 'lucide-react';
import { getWorkflowExecutions, getExecution } from '../services/api.ts';
import type { ExecutionDto, ExecutionStatus } from '../types/workflow.ts';

const statusConfig: Record<ExecutionStatus, { color: string; icon: React.ReactNode }> = {
  Pending: { color: 'gray', icon: <Clock size={14} /> },
  Running: { color: 'blue', icon: <LoaderIcon size={14} speed={2} /> },
  Completed: { color: 'green', icon: <Check size={14} strokeWidth={3} /> },
  Failed: { color: 'red', icon: <X size={14} strokeWidth={3} /> },
  Cancelled: { color: 'gray', icon: <X size={14} /> },
};

function formatDuration(startedAt: string | null, completedAt: string | null): string | null {
  if (!startedAt) return null;
  const start = new Date(startedAt).getTime();
  const end = completedAt ? new Date(completedAt).getTime() : Date.now();
  const ms = end - start;
  if (ms < 0) return null;
  if (ms < 1000) return `${ms}ms`;
  if (ms < 60000) return `${(ms / 1000).toFixed(1)}s`;
  const minutes = Math.floor(ms / 60000);
  const seconds = Math.floor((ms % 60000) / 1000);
  return `${minutes}m ${seconds}s`;
}

function formatDate(dateStr: string | null): string {
  if (!dateStr) return '-';
  return new Date(dateStr).toLocaleString();
}

export function ExecutionHistoryPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const [executions, setExecutions] = useState<ExecutionDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [statusFilter, setStatusFilter] = useState<string>('all');
  const [selectedExecution, setSelectedExecution] = useState<ExecutionDto | null>(null);

  const [refreshKey, setRefreshKey] = useState(0);
  const [page, setPage] = useState(1);
  const [expandedOutputs, setExpandedOutputs] = useState<Set<string>>(new Set());

  useEffect(() => {
    const fetchData = async () => {
      if (!id) return;
      setLoading(true);
      setError(null);
      try {
        const data = await getWorkflowExecutions(id);
        setExecutions(data);
      } catch (err) {
        setError(err instanceof Error ? err.message : 'Failed to fetch executions');
      } finally {
        setLoading(false);
      }
    };
    fetchData();
  }, [id, refreshKey]);

  const PAGE_SIZE = 20;

  const filteredExecutions = useMemo(() => {
    if (statusFilter === 'all') return executions;
    return executions.filter((e) => e.status === statusFilter);
  }, [executions, statusFilter]);

  const totalPages = Math.ceil(filteredExecutions.length / PAGE_SIZE);
  const paginatedExecutions = useMemo(() => {
    const start = (page - 1) * PAGE_SIZE;
    return filteredExecutions.slice(start, start + PAGE_SIZE);
  }, [filteredExecutions, page]);

  const handleViewExecution = async (execution: ExecutionDto) => {
    try {
      const detailed = await getExecution(execution.id);
      setSelectedExecution(detailed);
      setExpandedOutputs(new Set());
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to fetch execution details');
    }
  };

  const toggleOutput = (recordId: string) => {
    setExpandedOutputs((prev) => {
      const next = new Set(prev);
      if (next.has(recordId)) {
        next.delete(recordId);
      } else {
        next.add(recordId);
      }
      return next;
    });
  };

  const statusOptions = [
    { value: 'all', label: 'All Statuses' },
    { value: 'Completed', label: 'Completed' },
    { value: 'Failed', label: 'Failed' },
    { value: 'Running', label: 'Running' },
    { value: 'Pending', label: 'Pending' },
    { value: 'Cancelled', label: 'Cancelled' },
  ];

  return (
    <Stack gap="md" p="md">
      <Group justify="space-between" align="center">
        <Group gap="xs">
          <ActionIcon variant="subtle" onClick={() => navigate(-1)}>
            <ArrowLeft size={18} />
          </ActionIcon>
          <Text fw={600} size="lg">Execution History</Text>
        </Group>
        <Group gap="xs">
          <Select
            data={statusOptions}
            value={statusFilter}
            onChange={(value) => { setStatusFilter(value ?? 'all'); setPage(1); }}
            size="xs"
            w={140}
          />
          <ActionIcon variant="subtle" onClick={() => setRefreshKey(k => k + 1)}>
            <RefreshCw size={16} />
          </ActionIcon>
        </Group>
      </Group>

      <Divider />

      {loading && (
        <Group justify="center" py="xl">
          <Loader size="md" />
        </Group>
      )}

      {error && (
        <Text c="red" size="sm" ta="center" py="md">
          {error}
        </Text>
      )}

      {!loading && !error && filteredExecutions.length === 0 && (
        <Text c="dimmed" ta="center" py="xl">
          No executions found
        </Text>
      )}

      {!loading && !error && filteredExecutions.length > 0 && (
        <>
          <Table striped highlightOnHover>
            <Table.Thead>
              <Table.Tr>
                <Table.Th>Status</Table.Th>
                <Table.Th>Started</Table.Th>
                <Table.Th>Completed</Table.Th>
                <Table.Th>Duration</Table.Th>
                <Table.Th>Nodes</Table.Th>
                <Table.Th>Actions</Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {paginatedExecutions.map((execution) => {
                const statusInfo = statusConfig[execution.status] ?? statusConfig.Pending;
                const duration = formatDuration(execution.startedAt, execution.completedAt);
                return (
                  <Table.Tr key={execution.id}>
                    <Table.Td>
                      <Badge
                        color={statusInfo.color}
                        variant="light"
                        size="sm"
                        leftSection={statusInfo.icon}
                      >
                        {execution.status}
                      </Badge>
                    </Table.Td>
                    <Table.Td>{formatDate(execution.startedAt)}</Table.Td>
                    <Table.Td>{formatDate(execution.completedAt)}</Table.Td>
                    <Table.Td>{duration ?? '-'}</Table.Td>
                    <Table.Td>{execution.nodeRecords.length}</Table.Td>
                    <Table.Td>
                      <ActionIcon
                        variant="subtle"
                        size="sm"
                        onClick={() => handleViewExecution(execution)}
                      >
                        <Eye size={14} />
                      </ActionIcon>
                    </Table.Td>
                  </Table.Tr>
                );
              })}
            </Table.Tbody>
          </Table>
          {totalPages > 1 && (
            <Group justify="center">
              <Pagination
                value={page}
                onChange={setPage}
                total={totalPages}
                size="sm"
              />
            </Group>
          )}
        </>
      )}

      {selectedExecution && (
        <Paper p="md" withBorder>
          <Group justify="space-between" align="center" mb="md">
            <Text fw={600}>Execution Details</Text>
            <ActionIcon variant="subtle" onClick={() => setSelectedExecution(null)}>
              <X size={16} />
            </ActionIcon>
          </Group>
          <Stack gap="sm">
            <Group gap="md">
              <Text size="sm" c="dimmed">ID: {selectedExecution.id}</Text>
              <Badge
                color={statusConfig[selectedExecution.status]?.color ?? 'gray'}
                variant="light"
                size="sm"
              >
                {selectedExecution.status}
              </Badge>
            </Group>
            <Text size="sm" c="dimmed">
              Started: {formatDate(selectedExecution.startedAt)}
            </Text>
            <Text size="sm" c="dimmed">
              Completed: {formatDate(selectedExecution.completedAt)}
            </Text>
            {selectedExecution.nodeRecords.length > 0 && (
              <>
                <Divider />
                <Text fw={500} size="sm">Node Records</Text>
                <Table>
                  <Table.Thead>
                    <Table.Tr>
                      <Table.Th>Node</Table.Th>
                      <Table.Th>Status</Table.Th>
                      <Table.Th>Duration</Table.Th>
                      <Table.Th>Output</Table.Th>
                    </Table.Tr>
                  </Table.Thead>
                  <Table.Tbody>
                    {selectedExecution.nodeRecords.map((record) => {
                      const recordStatus = statusConfig[record.status] ?? statusConfig.Pending;
                      const recordDuration = formatDuration(record.startedAt, record.completedAt);
                      const isExpanded = expandedOutputs.has(record.id);
                      const outputStr = record.output != null
                        ? JSON.stringify(record.output, null, 2)
                        : null;
                      return (
                        <Table.Tr key={record.id}>
                          <Table.Td>{record.nodeDefinitionId.slice(0, 8)}</Table.Td>
                          <Table.Td>
                            <Badge
                              color={recordStatus.color}
                              variant="light"
                              size="xs"
                            >
                              {record.status}
                            </Badge>
                          </Table.Td>
                          <Table.Td>{recordDuration ?? '-'}</Table.Td>
                          <Table.Td>
                            {outputStr && (
                              <ActionIcon
                                variant="subtle"
                                size="xs"
                                onClick={() => toggleOutput(record.id)}
                              >
                                {isExpanded
                                  ? <ChevronDown size={12} />
                                  : <ChevronRight size={12} />
                                }
                              </ActionIcon>
                            )}
                          </Table.Td>
                        </Table.Tr>
                      );
                    })}
                  </Table.Tbody>
                </Table>
                {selectedExecution.nodeRecords.map((record) => {
                  const outputStr = record.output != null
                    ? JSON.stringify(record.output, null, 2)
                    : null;
                  if (!outputStr || !expandedOutputs.has(record.id)) return null;
                  return (
                    <Paper key={`output-${record.id}`} p="xs" withBorder mt="xs" bg="gray.0">
                      <Text size="xs" fw={500} mb={4}>
                        {record.nodeDefinitionId.slice(0, 8)} output:
                      </Text>
                      <pre style={{ margin: 0, fontSize: 'var(--mantine-font-size-xs)', whiteSpace: 'pre-wrap', wordBreak: 'break-all' }}>
                        {outputStr}
                      </pre>
                    </Paper>
                  );
                })}
              </>
            )}
          </Stack>
        </Paper>
      )}
    </Stack>
  );
}

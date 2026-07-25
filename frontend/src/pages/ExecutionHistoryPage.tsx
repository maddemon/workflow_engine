import { useState, Fragment } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import styles from './ExecutionHistoryPage.module.css';
import {
  Stack,
  Text,
  Group,
  ActionIcon,
  Table,
  Badge,
  Loader,
  Select,
  Divider,
  Pagination,
  Modal,
  Box,
} from '@mantine/core';
import { ArrowLeft, RefreshCw, Eye, ChevronDown, ChevronRight } from 'lucide-react';
import { useRequest } from 'ahooks';
import { useTranslation } from 'react-i18next';
import { getWorkflowExecutions, getExecution } from '../services/api.ts';
import type { ExecutionDto, ExecutionSummaryDto } from '../types/workflow.ts';
import { statusConfig, formatDuration } from '../utils/execution.tsx';
import { formatLocalDateTime } from '../utils/dateUtils.ts';

function formatDate(dateStr: string | null): string {
  return formatLocalDateTime(dateStr) || '-';
}

export function ExecutionHistoryPage() {
  const { t } = useTranslation('execution');
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const [statusFilter, setStatusFilter] = useState<string>('all');
  const [selectedExecution, setSelectedExecution] = useState<ExecutionDto | null>(null);

  const [page, setPage] = useState(1);
  const [expandedOutputs, setExpandedOutputs] = useState<Set<string>>(new Set());

  const { data, loading, error, refresh: refreshExecutions } = useRequest(
    () => getWorkflowExecutions(id!, {
      status: statusFilter === 'all' ? undefined : statusFilter,
      page,
      pageSize: PAGE_SIZE,
    }),
    { ready: !!id, refreshDeps: [id, page, statusFilter] },
  );

  const PAGE_SIZE = 20;

  const executions = data?.items ?? [];
  const totalPages = data?.totalPages ?? 0;

  const { loading: detailLoading, run: handleViewExecution } = useRequest(
    (execution: ExecutionSummaryDto) => getExecution(execution.id).then((detailed) => {
      setSelectedExecution(detailed);
      setExpandedOutputs(new Set());
    }),
    { manual: true },
  );

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
    { value: 'all', label: t('history.allStatuses') },
    { value: 'Completed', label: t('status.completed') },
    { value: 'Failed', label: t('status.failed') },
    { value: 'Running', label: t('status.running') },
    { value: 'Pending', label: t('status.pending') },
    { value: 'Cancelled', label: t('status.cancelled') },
  ];

  return (
    <Box h="100vh" style={{ overflow: 'auto' }}>
      <Stack gap="md" p="md">
        <Group justify="space-between" align="center">
          <Group gap="xs">
            <ActionIcon variant="subtle" onClick={() => navigate(-1)}>
              <ArrowLeft size={18} />
            </ActionIcon>
            <Text fw={600} size="lg">{t('history.title')}</Text>
          </Group>
          <Group gap="xs">
            <Select
              data={statusOptions}
              value={statusFilter}
              onChange={(value) => { setStatusFilter(value ?? 'all'); setPage(1); }}
              size="xs"
              w={140}
            />
            <ActionIcon variant="subtle" onClick={refreshExecutions}>
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
            {error.message ?? t('history.failedToFetch')}
          </Text>
        )}

        {!loading && !error && executions.length === 0 && (
          <Text c="dimmed" ta="center" py="xl">
            {t('history.noExecutionsFound')}
          </Text>
        )}

        {!loading && !error && executions.length > 0 && (
          <>
            <Table striped highlightOnHover>
              <Table.Thead>
                <Table.Tr>
                  <Table.Th>{t('history.status')}</Table.Th>
                  <Table.Th>{t('history.started')}</Table.Th>
                  <Table.Th>{t('history.completed')}</Table.Th>
                  <Table.Th>{t('history.duration')}</Table.Th>
                  <Table.Th>{t('history.actions')}</Table.Th>
                </Table.Tr>
              </Table.Thead>
              <Table.Tbody>
                {executions.map((execution) => {
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
                      <Table.Td>
                        <ActionIcon
                          variant="subtle"
                          size="sm"
                          onClick={() => handleViewExecution(execution)}
                          loading={detailLoading}
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
      </Stack>

      {/* Execution Details Modal */}
      <Modal
        opened={selectedExecution !== null}
        onClose={() => setSelectedExecution(null)}
        title={t('history.details')}
        size="lg"
      >
        {selectedExecution && (
          <Stack gap="md">
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
            {(selectedExecution.nodeRecords?.length ?? 0) > 0 && (
              <>
                <Divider />
                <Text fw={500} size="sm">{t('history.nodeRecords')}</Text>
                <Table>
                  <Table.Thead>
                    <Table.Tr>
                      <Table.Th>{t('history.node')}</Table.Th>
                      <Table.Th>{t('history.status')}</Table.Th>
                      <Table.Th>{t('history.duration')}</Table.Th>
                      <Table.Th>{t('history.output')}</Table.Th>
                    </Table.Tr>
                  </Table.Thead>
                  <Table.Tbody>
                    {(selectedExecution.nodeRecords ?? []).map((record) => {
                      const recordStatus = statusConfig[record.status] ?? statusConfig.Pending;
                      const recordDuration = formatDuration(record.startedAt, record.completedAt);
                      const isExpanded = expandedOutputs.has(record.id);
                      const outputStr = record.output != null
                        ? JSON.stringify(record.output, null, 2)
                        : null;
                      return (
                        <Fragment key={record.id}>
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
                          {isExpanded && outputStr && (
                            <Table.Tr key={`output-${record.id}`}>
                              <Table.Td colSpan={4} p={0}>
                                <Box
                                  p="xs"
                                  mx="xs"
                                  my={4}
                                  style={{
                                    backgroundColor: 'var(--mantine-color-dark-6)',
                                    borderRadius: 'var(--mantine-radius-sm)',
                                    border: '1px solid var(--mantine-color-dark-4)',
                                  }}
                                >
                                  <Text size="xs" fw={500} mb={4} c="dimmed">
                                    {record.nodeDefinitionId.slice(0, 8)} output:
                                  </Text>
                                  <pre className={styles.outputPre}>
                                    {outputStr}
                                  </pre>
                                </Box>
                              </Table.Td>
                            </Table.Tr>
                          )}
                        </Fragment>
                      );
                    })}
                  </Table.Tbody>
                </Table>
              </>
            )}
          </Stack>
        )}
      </Modal>
    </Box>
  );
}

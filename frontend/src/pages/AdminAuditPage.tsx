import { useState } from 'react';
import { Paper, Stack, Group, TextInput, Button, Table, Pagination, Text, Title, Badge } from '@mantine/core';
import { useTranslation } from 'react-i18next';
import { useRequest } from 'ahooks';
import { Search, X } from 'lucide-react';
import { queryAuditEvents, type AuditQueryParams } from '../services/api.ts';
import { AuditDetailDrawer } from '../components/admin/AuditDetailDrawer.tsx';

const PAGE_SIZE = 20;

function extractField(event: Record<string, unknown>, field: string): string {
  const val = event[field];
  if (val === null || val === undefined) return '—';
  if (typeof val === 'object') return JSON.stringify(val);
  return String(val);
}

function formatTime(iso: string | null | undefined): string {
  if (!iso) return '—';
  try {
    return new Date(iso).toLocaleString();
  } catch {
    return iso;
  }
}

export function AdminAuditPage() {
  const { t } = useTranslation('admin');
  const [eventType, setEventType] = useState('');
  const [resourceType, setResourceType] = useState('');
  const [resourceId, setResourceId] = useState('');
  const [from, setFrom] = useState('');
  const [to, setTo] = useState('');
  const [page, setPage] = useState(1);
  const [selectedEvent, setSelectedEvent] = useState<Record<string, unknown> | null>(null);
  const [drawerOpen, setDrawerOpen] = useState(false);

  const buildParams = (): AuditQueryParams => ({
    ...(eventType && { eventType }),
    ...(resourceType && { resourceType }),
    ...(resourceId && { resourceId }),
    ...(from && { from }),
    ...(to && { to }),
    offset: (page - 1) * PAGE_SIZE,
    limit: PAGE_SIZE,
  });

  const { data, loading, run } = useRequest(() => queryAuditEvents(buildParams()), {
    refreshDeps: [page, eventType, resourceType, resourceId, from, to],
    debounceWait: 300,
  });

  const handleSearch = () => {
    setPage(1);
    run();
  };

  const handleReset = () => {
    setEventType('');
    setResourceType('');
    setResourceId('');
    setFrom('');
    setTo('');
    setPage(1);
    run(); // explicit re-fetch ensures reset works even when already on page 1
  };

  const totalPages = data ? Math.ceil(data.total / PAGE_SIZE) : 0;

  return (
    <Stack p="md" gap="md">
      <Title order={3}>{t('auditPage.title')}</Title>

      <Paper withBorder p="sm" radius="sm">
        <Group gap="sm" wrap="wrap">
          <TextInput
            size="xs"
            placeholder={t('auditPage.eventType')}
            value={eventType}
            onChange={(e) => setEventType(e.currentTarget.value)}
          />
          <TextInput
            size="xs"
            placeholder={t('auditPage.resourceType')}
            value={resourceType}
            onChange={(e) => setResourceType(e.currentTarget.value)}
          />
          <TextInput
            size="xs"
            placeholder={t('auditPage.resourceId')}
            value={resourceId}
            onChange={(e) => setResourceId(e.currentTarget.value)}
          />
          <TextInput
            size="xs"
            placeholder={t('auditPage.from')}
            value={from}
            onChange={(e) => setFrom(e.currentTarget.value)}
          />
          <TextInput
            size="xs"
            placeholder={t('auditPage.to')}
            value={to}
            onChange={(e) => setTo(e.currentTarget.value)}
          />
          <Button size="xs" leftSection={<Search size={14} />} onClick={handleSearch} loading={loading}>
            {t('auditPage.search')}
          </Button>
          <Button size="xs" variant="subtle" leftSection={<X size={14} />} onClick={handleReset}>
            {t('auditPage.reset')}
          </Button>
        </Group>
      </Paper>

      <Paper withBorder radius="sm">
        <Table striped highlightOnHover>
          <Table.Thead>
            <Table.Tr>
              <Table.Th>{t('auditPage.eventType')}</Table.Th>
              <Table.Th>{t('auditPage.resourceType')}</Table.Th>
              <Table.Th>{t('auditPage.resourceId')}</Table.Th>
              <Table.Th>{t('auditPage.timestamp')}</Table.Th>
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {data?.events.map((event, idx) => (
              <Table.Tr
                key={idx}
                tabIndex={0}
                role="button"
                style={{ cursor: 'pointer' }}
                onClick={() => {
                  setSelectedEvent(event);
                  setDrawerOpen(true);
                }}
                onKeyDown={(e) => {
                  if (e.key === 'Enter' || e.key === ' ') {
                    e.preventDefault();
                    setSelectedEvent(event);
                    setDrawerOpen(true);
                  }
                }}
              >
                <Table.Td>
                  <Badge size="sm" variant="light">
                    {extractField(event, 'eventType')}
                  </Badge>
                </Table.Td>
                <Table.Td>{extractField(event, 'resourceType')}</Table.Td>
                <Table.Td>
                  <Text size="sm" style={{ fontFamily: 'monospace', fontSize: 12 }}>
                    {extractField(event, 'resourceId')}
                  </Text>
                </Table.Td>
                <Table.Td>{formatTime(extractField(event, 'timestamp'))}</Table.Td>
              </Table.Tr>
            ))}
            {(!data || data.events.length === 0) && !loading && (
              <Table.Tr>
                <Table.Td colSpan={4}><Text ta="center" c="dimmed" py="md">{t('auditPage.noEvents')}</Text></Table.Td>
              </Table.Tr>
            )}
          </Table.Tbody>
        </Table>
        {totalPages > 0 && (
          <Group justify="center" p="sm">
            <Pagination total={totalPages} value={page} onChange={setPage} size="sm" />
          </Group>
        )}
      </Paper>

      <AuditDetailDrawer
        opened={drawerOpen}
        onClose={() => setDrawerOpen(false)}
        event={selectedEvent}
      />
    </Stack>
  );
}

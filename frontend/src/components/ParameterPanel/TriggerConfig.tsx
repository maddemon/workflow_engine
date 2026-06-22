import { useState, useEffect, useCallback } from 'react';
import {
  Stack, TextInput, Select, Switch, Button, Group, Text,
  ActionIcon, Collapse, UnstyledButton, Modal, Paper,
} from '@mantine/core';
import { notifications } from '@mantine/notifications';
import { ChevronDown, ChevronRight, Plus, Trash, Edit, Clock, Webhook } from 'lucide-react';
import { InfoTooltip } from './fields/InfoTooltip.tsx';
import type { TriggerDto, TriggerSettingsDto } from '../../types/workflow.ts';
import { useWorkflowStore } from '../../stores/workflowStore.ts';
import * as api from '../../services/api.ts';

interface TriggerConfigProps {
  workflowId: string;
  isExecuting: boolean;
}

export function TriggerConfig({ workflowId, isExecuting }: TriggerConfigProps) {
  const [triggers, setTriggers] = useState<TriggerDto[]>([]);
  const workflowVersion = useWorkflowStore((s) => s.workflowVersion);
  const [loading, setLoading] = useState(false);
  const [showForm, setShowForm] = useState(false);
  const [editTrigger, setEditTrigger] = useState<TriggerDto | null>(null);
  const [expanded, setExpanded] = useState(false);

  // Form state
  const [type, setType] = useState<'Schedule' | 'Webhook'>('Schedule');
  const [name, setName] = useState('');
  const [isActive, setIsActive] = useState(true);
  const [cronExpression, setCronExpression] = useState('');
  const [timeZone, setTimeZone] = useState('UTC');
  const [webhookPath, setWebhookPath] = useState('');
  const [secret, setSecret] = useState('');
  const [allowedIps, setAllowedIps] = useState('');
  const [allowedOrigins, setAllowedOrigins] = useState('');
  const [isSync, setIsSync] = useState(false);

  const loadTriggers = useCallback(async () => {
    if (!workflowId) return;
    setLoading(true);
    try {
      const data = await api.getTriggers(workflowId);
      setTriggers(data);
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Failed to load triggers';
      notifications.show({ title: 'Error', message: msg, color: 'red' });
    } finally {
      setLoading(false);
    }
  }, [workflowId]);

  useEffect(() => {
    loadTriggers();
  }, [loadTriggers]);

  const resetForm = () => {
    setType('Schedule');
    setName('');
    setIsActive(true);
    setCronExpression('');
    setTimeZone('UTC');
    setWebhookPath('');
    setSecret('');
    setAllowedIps('');
    setAllowedOrigins('');
    setIsSync(false);
    setEditTrigger(null);
  };

  const openCreateForm = () => {
    resetForm();
    setShowForm(true);
  };

  const openEditForm = (trigger: TriggerDto) => {
    setEditTrigger(trigger);
    setType(trigger.type);
    setName(trigger.name);
    setIsActive(trigger.isActive);
    setCronExpression(trigger.settings?.cronExpression ?? '');
    setTimeZone(trigger.settings?.timeZone ?? 'UTC');
    setWebhookPath(trigger.settings?.webhookPath ?? '');
    setSecret(trigger.settings?.secret ?? '');
    setAllowedIps(trigger.settings?.allowedIps?.join(', ') ?? '');
    setAllowedOrigins(trigger.settings?.allowedOrigins?.join(', ') ?? '');
    setIsSync(trigger.settings?.isSync ?? false);
    setShowForm(true);
  };

  const handleSubmit = async () => {
    const settings: TriggerSettingsDto = type === 'Schedule'
      ? { cronExpression, timeZone, startAt: null, endAt: null }
      : {
          webhookPath,
          secret: secret || undefined,
          allowedIps: allowedIps ? allowedIps.split(',').map((s) => s.trim()).filter(Boolean) : undefined,
          allowedOrigins: allowedOrigins ? allowedOrigins.split(',').map((s) => s.trim()).filter(Boolean) : undefined,
          isSync,
          maxWaitSeconds: 30,
        };

    try {
      if (editTrigger) {
        await api.updateTrigger(workflowId, editTrigger.id, { name, isActive, settings });
        notifications.show({ title: 'Updated', message: 'Trigger updated', color: 'blue' });
      } else {
        await api.createTrigger(workflowId, {
          workflowDefinitionId: workflowId,
          workflowVersion,
          type,
          name,
          isActive,
          settings,
        });
        notifications.show({ title: 'Created', message: 'Trigger created', color: 'green' });
      }
      setShowForm(false);
      resetForm();
      loadTriggers();
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Operation failed';
      notifications.show({ title: 'Error', message: msg, color: 'red' });
    }
  };

  const handleDelete = async (triggerId: string) => {
    try {
      await api.deleteTrigger(workflowId, triggerId);
      notifications.show({ title: 'Deleted', message: 'Trigger deleted', color: 'orange' });
      loadTriggers();
    } catch {
      notifications.show({ title: 'Error', message: 'Failed to delete trigger', color: 'red' });
    }
  };

  return (
    <>
      <UnstyledButton w="100%" onClick={() => setExpanded(!expanded)} py={4}>
        <Group justify="space-between" wrap="nowrap">
          <Group gap={4}>
            <Text size="xs" fw={600}>Triggers</Text>
            <InfoTooltip label="Configure schedule or webhook triggers" />
          </Group>
          <Group gap={4}>
            <BadgeCount count={triggers.length} />
            {expanded ? <ChevronDown size={12} /> : <ChevronRight size={12} />}
          </Group>
        </Group>
      </UnstyledButton>
      <Collapse expanded={expanded}>
        <Stack gap="xs" pb="sm">
          {triggers.length === 0 && !loading && (
            <Text size="xs" c="dimmed" ta="center" py="sm">No triggers configured</Text>
          )}
          {triggers.map((t) => (
            <Paper key={t.id} p="xs" withBorder style={{ position: 'relative' }}>
              <Group justify="space-between" wrap="nowrap">
                <Group gap={4} wrap="nowrap" style={{ flex: 1, minWidth: 0 }}>
                  {t.type === 'Schedule' ? <Clock size={14} /> : <Webhook size={14} />}
                  <Text size="xs" truncate style={{ flex: 1 }}>{t.name}</Text>
                  <Text size="xs" c="dimmed">{t.type}</Text>
                </Group>
                <Group gap={2} wrap="nowrap">
                  <ActionIcon variant="subtle" size="sm" onClick={() => openEditForm(t)} disabled={isExecuting}>
                    <Edit size={12} />
                  </ActionIcon>
                  <ActionIcon variant="subtle" color="red" size="sm" onClick={() => handleDelete(t.id)} disabled={isExecuting}>
                    <Trash size={12} />
                  </ActionIcon>
                </Group>
              </Group>
              {t.type === 'Schedule' && t.settings?.cronExpression && (
                <Text size="xs" ff="monospace" c="dimmed" mt={2}>
                  Cron: {t.settings.cronExpression}
                  {t.nextTriggerAt && <> · Next: {new Date(t.nextTriggerAt).toLocaleString()}</>}
                </Text>
              )}
              {t.type === 'Webhook' && (
                <Text size="xs" ff="monospace" c="dimmed" mt={2}>
                  {t.settings?.webhookPath ?? '-'}
                </Text>
              )}
            </Paper>
          ))}
          <Button
            variant="light"
            size="compact-sm"
            leftSection={<Plus size={12} />}
            onClick={openCreateForm}
            disabled={isExecuting}
          >
            Add Trigger
          </Button>
        </Stack>
      </Collapse>

      <Modal
        opened={showForm}
        onClose={() => { setShowForm(false); resetForm(); }}
        title={editTrigger ? 'Edit Trigger' : 'New Trigger'}
        size="sm"
      >
        <Stack gap="sm">
          <Select
            label="Type"
            value={type}
            onChange={(v) => setType((v as 'Schedule' | 'Webhook') ?? 'Schedule')}
            data={[
              { label: 'Schedule (Cron)', value: 'Schedule' },
              { label: 'Webhook', value: 'Webhook' },
            ]}
            disabled={!!editTrigger}
          />
          <TextInput
            label="Name"
            value={name}
            onChange={(e) => setName(e.target.value)}
            required
          />
          <Switch checked={isActive} onChange={(e) => setIsActive(e.currentTarget.checked)} label="Active" size="sm" />

          {type === 'Schedule' && (
            <>
              <TextInput
                label="Cron Expression"
                value={cronExpression}
                onChange={(e) => setCronExpression(e.target.value)}
                placeholder="*/5 * * * *"
                description="Standard cron format: sec min hour day mon dow"
              />
              <Select
                label="Time Zone"
                value={timeZone}
                onChange={(v) => setTimeZone(v ?? 'UTC')}
                data={[
                  { label: 'UTC', value: 'UTC' },
                  { label: 'Asia/Shanghai', value: 'Asia/Shanghai' },
                  { label: 'America/New_York', value: 'America/New_York' },
                  { label: 'Europe/London', value: 'Europe/London' },
                ]}
              />
            </>
          )}

          {type === 'Webhook' && (
            <>
              <TextInput
                label="Webhook Path"
                value={webhookPath}
                onChange={(e) => setWebhookPath(e.target.value)}
                placeholder="my-webhook"
                description="Public URL path: /webhooks/{path}"
              />
              <TextInput
                label="Secret (optional)"
                type="password"
                value={secret}
                onChange={(e) => setSecret(e.target.value)}
                placeholder="HMAC-SHA256 secret"
              />
              <TextInput
                label="Allowed IPs (comma separated)"
                value={allowedIps}
                onChange={(e) => setAllowedIps(e.target.value)}
                placeholder="192.168.1.0/24, 10.0.0.1"
              />
              <TextInput
                label="Allowed Origins (comma separated)"
                value={allowedOrigins}
                onChange={(e) => setAllowedOrigins(e.target.value)}
                placeholder="example.com"
              />
              <Switch checked={isSync} onChange={(e) => setIsSync(e.currentTarget.checked)} label="Synchronous response" size="sm" />
            </>
          )}

          <Group justify="flex-end" mt="sm">
            <Button variant="default" size="compact-sm" onClick={() => { setShowForm(false); resetForm(); }}>
              Cancel
            </Button>
            <Button size="compact-sm" onClick={handleSubmit}>
              {editTrigger ? 'Update' : 'Create'}
            </Button>
          </Group>
        </Stack>
      </Modal>
    </>
  );
}

function BadgeCount({ count }: { count: number }) {
  return (
    <Text size="xs" c="dimmed" style={{ minWidth: 18, textAlign: 'center' }}>
      {count}
    </Text>
  );
}

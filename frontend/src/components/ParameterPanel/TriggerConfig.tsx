import { useState } from 'react';
import {
  Stack, TextInput, Select, Switch, Button, Group, Text,
  ActionIcon, Collapse, UnstyledButton, Modal, Paper,
} from '@mantine/core';
import { notifications } from '@mantine/notifications';
import { ChevronDown, ChevronRight, Plus, Trash, Edit, Clock, Webhook } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { useRequest } from 'ahooks';
import { InfoTooltip } from './fields/InfoTooltip.tsx';
import { CronBuilder } from './fields/CronBuilder.tsx';
import { formatLocalDateTime } from '../../utils/dateUtils.ts';
import type { TriggerDto, TriggerSettingsDto } from '../../types/workflow.ts';
import { useWorkflowStore } from '../../stores/workflowStore.ts';
import * as api from '../../services/api.ts';

interface TriggerConfigProps {
  workflowId: string;
  isExecuting: boolean;
  reviewMode?: boolean;
}

export function TriggerConfig({ workflowId, isExecuting, reviewMode }: TriggerConfigProps) {
  const { t } = useTranslation('parameterPanel');
  const workflowVersion = useWorkflowStore((s) => s.workflowVersion);
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

  const { data: triggers = [], loading, refresh: refreshTriggers } = useRequest(
    () => api.getTriggers(workflowId),
    { ready: !!workflowId },
  );

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
    if (trigger.type !== 'Schedule' && trigger.type !== 'Webhook') {
      notifications.show({
        title: t('triggerConfig.notSupported'),
        message: t('triggerConfig.notSupportedMessage', { type: trigger.type }),
        color: 'orange',
      });
      return;
    }
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
        notifications.show({ title: t('triggerConfig.updated'), message: t('triggerConfig.updatedMessage'), color: 'blue' });
      } else {
        await api.createTrigger(workflowId, {
          workflowDefinitionId: workflowId,
          workflowVersion,
          type,
          name,
          isActive,
          settings,
        });
        notifications.show({ title: t('triggerConfig.created'), message: t('triggerConfig.createdMessage'), color: 'green' });
      }
      setShowForm(false);
      resetForm();
      refreshTriggers();
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : t('triggerConfig.operationFailed');
      notifications.show({ title: t('triggerConfig.error'), message: msg, color: 'red' });
    }
  };

  const handleDelete = async (triggerId: string) => {
    try {
      await api.deleteTrigger(workflowId, triggerId);
      notifications.show({ title: t('triggerConfig.deleted'), message: t('triggerConfig.deletedMessage'), color: 'orange' });
      refreshTriggers();
    } catch {
      notifications.show({ title: t('triggerConfig.error'), message: t('triggerConfig.deleteFailed'), color: 'red' });
    }
  };

  return (
    <>
      <UnstyledButton w="100%" onClick={() => setExpanded(!expanded)} py={4}>
        <Group justify="space-between" wrap="nowrap">
          <Group gap={4}>
            <Text size="xs" fw={600}>{t('triggerConfig.title')}</Text>
            <InfoTooltip label={t('triggerConfig.tooltip')} />
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
            <Text size="xs" c="dimmed" ta="center" py="sm">{t('triggerConfig.noTriggers')}</Text>
          )}
          {triggers.map((trigger) => (
            <Paper key={trigger.id} p="xs" withBorder style={{ position: 'relative' }}>
              <Group justify="space-between" wrap="nowrap">
                <Group gap={4} wrap="nowrap" style={{ flex: 1, minWidth: 0 }}>
                  {trigger.type === 'Schedule' ? <Clock size={14} /> : <Webhook size={14} />}
                  <Text size="xs" truncate style={{ flex: 1 }}>{trigger.name}</Text>
                  <Text size="xs" c="dimmed">{trigger.type}</Text>
                </Group>
                <Group gap={2} wrap="nowrap">
                <ActionIcon variant="subtle" size="sm" onClick={() => openEditForm(trigger)} disabled={isExecuting || reviewMode}>
                  <Edit size={12} />
                </ActionIcon>
                <ActionIcon variant="subtle" color="red" size="sm" onClick={() => handleDelete(trigger.id)} disabled={isExecuting || reviewMode}>
                    <Trash size={12} />
                  </ActionIcon>
                </Group>
              </Group>
              {trigger.type === 'Schedule' && trigger.settings?.cronExpression && (
                <Text size="xs" ff="monospace" c="dimmed" mt={2}>
                  Cron: {trigger.settings.cronExpression}
                  {trigger.nextTriggerAt && <> · Next: {formatLocalDateTime(trigger.nextTriggerAt)}</>}
                </Text>
              )}
              {trigger.type === 'Webhook' && (
                <Text size="xs" ff="monospace" c="dimmed" mt={2}>
                  {trigger.settings?.webhookPath ?? '-'}
                </Text>
              )}
            </Paper>
          ))}
          <Button
            variant="light"
            size="compact-sm"
            leftSection={<Plus size={12} />}
            onClick={openCreateForm}
            disabled={isExecuting || reviewMode}
          >
            {t('triggerConfig.addTrigger')}
          </Button>
        </Stack>
      </Collapse>

      <Modal
        opened={showForm}
        onClose={() => { setShowForm(false); resetForm(); }}
        title={editTrigger ? t('triggerConfig.editTrigger') : t('triggerConfig.newTrigger')}
        size="sm"
      >
        <Stack gap="sm">
          <Select
            label={t('triggerConfig.type')}
            value={type}
            onChange={(v) => setType((v as 'Schedule' | 'Webhook') ?? 'Schedule')}
            data={[
              { label: t('triggerConfig.scheduleCron'), value: 'Schedule' },
              { label: t('triggerConfig.webhook'), value: 'Webhook' },
            ]}
            disabled={!!editTrigger}
          />
          <TextInput
            label={t('triggerConfig.name')}
            value={name}
            onChange={(e) => setName(e.target.value)}
            required
          />
          <Switch checked={isActive} onChange={(e) => setIsActive(e.currentTarget.checked)} label={t('triggerConfig.active')} size="sm" />

          {type === 'Schedule' && (
            <>
              <CronBuilder
                value={cronExpression}
                onChange={setCronExpression}
                disabled={isExecuting}
              />
              <Select
                label={t('triggerConfig.timeZone')}
                value={timeZone}
                onChange={(v) => setTimeZone(v ?? 'UTC')}
                data={[
                  { label: 'UTC', value: 'UTC' },
                  { label: 'Asia/Shanghai', value: 'Asia/Shanghai' },
                  { label: 'America/New_York', value: 'America/New_York' },
                  { label: 'Europe/London', value: 'Europe/London' },
                ]}
                disabled={isExecuting}
              />
            </>
          )}

          {type === 'Webhook' && (
            <>
              <TextInput
                label={t('triggerConfig.webhookPath')}
                value={webhookPath}
                onChange={(e) => setWebhookPath(e.target.value)}
                placeholder={t('triggerConfig.webhookPathPlaceholder')}
                description={t('triggerConfig.webhookPathDescription')}
              />
              <TextInput
                label={t('triggerConfig.secret')}
                type="password"
                value={secret}
                onChange={(e) => setSecret(e.target.value)}
                placeholder={t('triggerConfig.secretPlaceholder')}
              />
              <TextInput
                label={t('triggerConfig.allowedIps')}
                value={allowedIps}
                onChange={(e) => setAllowedIps(e.target.value)}
                placeholder={t('triggerConfig.allowedIpsPlaceholder')}
              />
              <TextInput
                label={t('triggerConfig.allowedOrigins')}
                value={allowedOrigins}
                onChange={(e) => setAllowedOrigins(e.target.value)}
                placeholder={t('triggerConfig.allowedOriginsPlaceholder')}
              />
              <Switch checked={isSync} onChange={(e) => setIsSync(e.currentTarget.checked)} label={t('triggerConfig.synchronousResponse')} size="sm" />
            </>
          )}

          <Group justify="flex-end" mt="sm">
            <Button variant="default" size="compact-sm" onClick={() => { setShowForm(false); resetForm(); }}>
              {t('triggerConfig.cancel')}
            </Button>
            <Button size="compact-sm" onClick={handleSubmit}>
              {editTrigger ? t('triggerConfig.update') : t('triggerConfig.create')}
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

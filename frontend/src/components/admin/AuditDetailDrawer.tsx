import { Drawer } from '@mantine/core';
import { useTranslation } from 'react-i18next';
import { CodeViewer } from '../ExecutionPanel/CodeViewer.tsx';

interface AuditDetailDrawerProps {
  opened: boolean;
  onClose: () => void;
  event: Record<string, unknown> | null;
}

export function AuditDetailDrawer({ opened, onClose, event }: AuditDetailDrawerProps) {
  const { t } = useTranslation('admin');
  return (
    <Drawer opened={opened} onClose={onClose} title={t('auditDrawer.title')} size="lg" position="right">
      {event && (
        <CodeViewer code={JSON.stringify(event, null, 2)} language="json" label={t('auditDrawer.eventDetails')} />
      )}
    </Drawer>
  );
}

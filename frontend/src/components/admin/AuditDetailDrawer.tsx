import { Drawer } from '@mantine/core';
import { CodeViewer } from '../ExecutionPanel/CodeViewer.tsx';

interface AuditDetailDrawerProps {
  opened: boolean;
  onClose: () => void;
  event: Record<string, unknown> | null;
}

export function AuditDetailDrawer({ opened, onClose, event }: AuditDetailDrawerProps) {
  return (
    <Drawer opened={opened} onClose={onClose} title="Audit Event Details" size="lg" position="right">
      {event && (
        <CodeViewer code={JSON.stringify(event, null, 2)} language="json" label="Event Details" />
      )}
    </Drawer>
  );
}

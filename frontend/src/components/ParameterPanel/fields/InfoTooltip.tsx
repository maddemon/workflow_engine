import { Tooltip } from '@mantine/core';
import { Info } from 'lucide-react';

interface InfoTooltipProps {
  label: string;
}

export function InfoTooltip({ label }: InfoTooltipProps) {
  return (
    <Tooltip label={label} position="top" withArrow multiline maw={220}>
      <span
        style={{
          display: 'inline-flex',
          alignItems: 'center',
          justifyContent: 'center',
          width: 14,
          height: 14,
          flexShrink: 0,
          cursor: 'default',
          color: 'var(--mantine-color-dimmed)',
          verticalAlign: 'middle',
          marginTop: -1,
        }}
      >
        <Info size={12} />
      </span>
    </Tooltip>
  );
}

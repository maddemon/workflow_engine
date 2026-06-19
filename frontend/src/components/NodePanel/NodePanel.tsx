import { useState, useMemo, useCallback } from 'react';
import { Stack, TextInput, Text, Badge, UnstyledButton, Group, Box } from '@mantine/core';
import { useWorkflowStore } from '../../stores/workflowStore.ts';
import { NodeCard } from './NodeCard.tsx';

export function NodePanel() {
  const nodeTypes = useWorkflowStore((s) => s.nodeTypes);
  const addNode = useWorkflowStore((s) => s.addNode);
  const [search, setSearch] = useState('');
  const [collapsed, setCollapsed] = useState<Record<string, boolean>>({});

  const filtered = useMemo(() => {
    if (!search.trim()) return nodeTypes;
    const lower = search.toLowerCase();
    return nodeTypes.filter(
      (t) =>
        t.displayName.toLowerCase().includes(lower) ||
        t.typeName.toLowerCase().includes(lower) ||
        t.category.toLowerCase().includes(lower),
    );
  }, [nodeTypes, search]);

  const grouped = useMemo(() => {
    const map = new Map<string, typeof filtered>();
    for (const t of filtered) {
      const list = map.get(t.category) ?? [];
      list.push(t);
      map.set(t.category, list);
    }
    return map;
  }, [filtered]);

  const toggleCategory = useCallback((category: string) => {
    setCollapsed((prev) => ({ ...prev, [category]: !prev[category] }));
  }, []);

  const handleAddNode = useCallback(
    (typeName: string) => {
      addNode(typeName, { x: 250 + Math.random() * 200, y: 150 + Math.random() * 200 });
    },
    [addNode],
  );

  return (
    <Stack gap="sm" p="sm">
      <Text fw={600} size="md">Nodes</Text>
      <TextInput
        placeholder="Search nodes..."
        value={search}
        onChange={(e) => setSearch(e.target.value)}
        size="sm"
      />
      <Text size="xs" c="dimmed">
        Drag a node to canvas, or click to add. Connect ports by dragging from an output dot to an input dot.
      </Text>

      <Stack gap="xs">
        {Array.from(grouped.entries()).map(([category, types]) => (
          <Box key={category}>
            <UnstyledButton
              w="100%"
              px="xs"
              py={6}
              onClick={() => toggleCategory(category)}
              style={{ borderRadius: 4 }}
            >
              <Group gap="xs" wrap="nowrap">
                <Text size="xs" c="dimmed" style={{ width: 12 }}>
                  {collapsed[category] ? '▶' : '▼'}
                </Text>
                <Text size="sm" fw={600} flex={1}>{category}</Text>
                <Badge size="xs" variant="light" color="gray">{types.length}</Badge>
              </Group>
            </UnstyledButton>
            {!collapsed[category] && (
              <Stack gap={2} mt={2}>
                {types.map((t) => (
                  <NodeCard key={t.typeName} descriptor={t} onClick={handleAddNode} />
                ))}
              </Stack>
            )}
          </Box>
        ))}
        {grouped.size === 0 && (
          <Text size="sm" c="dimmed" ta="center" py="md">No nodes found</Text>
        )}
      </Stack>
    </Stack>
  );
}

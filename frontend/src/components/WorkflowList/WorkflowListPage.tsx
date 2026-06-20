import { useState, useEffect, useCallback } from 'react';
import { Stack, Text, SimpleGrid, Button, Loader, Center, Alert, Group, ThemeIcon } from '@mantine/core';
import { Plus, AlertCircle, Workflow } from 'lucide-react';
import { useNavigate } from 'react-router-dom';
import { getWorkflows } from '../../services/api.ts';
import { useWorkflowStore } from '../../stores/workflowStore.ts';
import { WorkflowCard } from './WorkflowCard.tsx';
import type { WorkflowSummary } from '../../types/workflow.ts';

export function WorkflowListPage() {
  const [workflows, setWorkflows] = useState<WorkflowSummary[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const navigate = useNavigate();
  const newWorkflow = useWorkflowStore((s) => s.newWorkflow);
  const deleteWorkflow = useWorkflowStore((s) => s.deleteWorkflow);

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

  const handleOpen = (id: string) => {
    navigate(`/workflow/${id}`);
  };

  const handleNew = () => {
    newWorkflow();
    navigate('/workflow/new');
  };

  const handleDelete = async (id: string) => {
    if (!confirm('Are you sure you want to delete this workflow?')) {
      return;
    }
    try {
      await deleteWorkflow(id);
      setWorkflows((prev) => prev.filter((w) => w.id !== id));
    } catch (err) {
      alert(err instanceof Error ? err.message : 'Failed to delete workflow');
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
      <Group justify="space-between">
        <Text fw={700} size="lg">Workflows</Text>
        <Button leftSection={<Plus size={16} />} onClick={handleNew}>
          New Workflow
        </Button>
      </Group>

      {workflows.length === 0 ? (
        <Center h="60%">
          <Stack align="center" gap="md">
            <ThemeIcon size={64} radius="xl" variant="light" color="gray">
              <Workflow size={32} />
            </ThemeIcon>
            <Text c="dimmed" size="sm">No workflows yet.</Text>
            <Button leftSection={<Plus size={16} />} onClick={handleNew}>
              Create your first workflow
            </Button>
          </Stack>
        </Center>
      ) : (
        <SimpleGrid cols={{ base: 1, sm: 2, lg: 3 }} spacing="md">
          {workflows.map((wf) => (
            <WorkflowCard
              key={wf.id}
              workflow={wf}
              onClick={handleOpen}
              onDelete={handleDelete}
            />
          ))}
        </SimpleGrid>
      )}
    </Stack>
  );
}

import { useState, useEffect } from 'react';
import {
  Stack,
  Text,
  Title,
  Code,
  Paper,
  List,
  CopyButton,
  Group,
  Button,
  ThemeIcon,
} from '@mantine/core';
import { useRequest } from 'ahooks';
import { Check, Copy } from 'lucide-react';
import * as api from '../services/api.ts';
import type { CreateApiKeyResult } from '../types/workflow.ts';

const CLAUDE_SKILL = `---
name: flow-engine-workflows
description: Use when the user wants to build, modify, validate, or run workflows in Flow Engine via natural language.
---

# Flow Engine Workflows

Flow Engine exposes its capabilities through an MCP server. When the user asks to
create or change a workflow, drive Flow Engine through the MCP tools instead of
editing JSON by hand.

## Available tools
- assemble_workflow — turn a natural-language spec into a full draft workflow
- modify_workflow — apply a change request to an existing workflow
- validate_workflow — check a draft for errors before activating
- confirm_workflow — activate a reviewed draft
- reject_draft — send rejection feedback back to the AI author
- get_draft_feedback — read pending review feedback for a draft

## Workflow
1. If no workflow exists yet, call assemble_workflow with the user's intent.
2. For tweaks, call modify_workflow and describe the change in plain language.
3. Always validate_workflow before confirming. Surface any errors to the user.
4. Once the user is happy, confirm_workflow to activate it.
5. If the user rejects a draft, capture the reason and call reject_draft.`;

function ConfigBlock({ code, label }: { code: string; label: string }) {
  return (
    <Paper p="sm" withBorder>
      <Group justify="space-between" mb={4}>
        <Text size="xs" fw={600} tt="uppercase" c="dimmed">{label}</Text>
        <CopyButton value={code}>
          {({ copied, copy }) => (
            <Button size="compact-xs" variant="subtle" leftSection={copied ? <Check size={12} /> : <Copy size={12} />} onClick={copy}>
              {copied ? 'Copied' : 'Copy'}
            </Button>
          )}
        </CopyButton>
      </Group>
      <Code block>{code}</Code>
    </Paper>
  );
}

/** 根据真实 API Key 动态生成可直接复制的 MCP 客户端配置。 */
function McpConfigBlock({ apiKey }: { apiKey: string }) {
  const config = JSON.stringify(
    {
      mcpServers: {
        'flow-engine': {
          type: 'http',
          url: 'http://localhost:5000/mcp',
          headers: { Authorization: `Bearer ${apiKey}` },
        },
      },
    },
    null,
    2,
  );
  return <ConfigBlock code={config} label="mcp.json" />;
}

// 复用同一浏览器会话内已生成的 Key，避免反复进入页面时不断新建。
let sessionKey: CreateApiKeyResult | null = null;

/**
 * 文档页内联的 API Key 管理：加载时若无可用 Key 则自动生成一个，并直接展示明文
 * （仅本次会话展示，可复制），同时把真实 Key 嵌入上方 MCP 配置，用户无需任何手动操作。
 */
function ApiKeyManager() {
  const [key, setKey] = useState<CreateApiKeyResult | null>(sessionKey);
  const { runAsync: createKey, loading } = useRequest(api.createApiKey, { manual: true });

  useEffect(() => {
    if (sessionKey) return;
    (async () => {
      const result = await createKey(`flow-engine-${new Date().toISOString().slice(0, 10)}`);
      sessionKey = result;
      setKey(result);
    })();
  }, [createKey]);

  if (loading && !key) {
    return <Text size="sm" c="dimmed">Generating API key…</Text>;
  }
  if (!key) return null;

  return (
    <Stack gap="md">
      <Paper p="sm" withBorder bg="green.0">
        <Group justify="space-between" mb={4}>
          <Text size="xs" fw={600} tt="uppercase" c="green.8">Your API key</Text>
          <CopyButton value={key.key}>
            {({ copied, copy }) => (
              <Button size="compact-xs" variant="subtle" color="green" leftSection={copied ? <Check size={12} /> : <Copy size={12} />} onClick={copy}>
                {copied ? 'Copied' : 'Copy'}
              </Button>
            )}
          </CopyButton>
        </Group>
        <Code block>{key.key}</Code>
        <Text size="xs" c="green.9" mt={4}>
          Copy and store it securely — it is shown only for this browser session.
        </Text>
      </Paper>
      <McpConfigBlock apiKey={key.key} />
    </Stack>
  );
}

export function HelpPage() {
  return (
    <div style={{ height: '100%', overflowY: 'auto' }}>
      <Stack p="md" gap="lg" style={{ maxWidth: 860, margin: '0 auto' }}>
        <Title order={2}>Help &amp; MCP Configuration</Title>

        <Paper p="md" withBorder>
          <Title order={4} mb="sm">What is MCP?</Title>
          <Text size="sm">
            MCP (Model Context Protocol) lets AI clients such as Claude Desktop, Cursor,
            and CodeBuddy connect to Flow Engine and build, modify, validate, and run
            workflows through natural language instead of editing JSON directly.
          </Text>
        </Paper>

        <Paper p="md" withBorder>
          <Title order={4} mb="sm">Connecting your AI client</Title>
          <Text size="sm" mb="sm">
            Flow Engine exposes an MCP server over Streamable HTTP at the <Code>/mcp</Code> endpoint.
            A personal API key is generated for you automatically below and embedded into the
            client config — copy the config and drop it into your AI client:
          </Text>
          <ApiKeyManager />
        </Paper>

        <Paper p="md" withBorder>
          <Title order={4} mb="sm">Agent skill</Title>
          <Text size="sm" mb="sm">
            Drop the Skill definition below into your agent so it knows how to drive Flow Engine
            through MCP. The file path convention is <Code>/.&lt;ide name&gt;/skills/flow-engine/SKILL.md</Code> —
            for example:
          </Text>
          <ConfigBlock code={`~/.cursor/skills/flow-engine/SKILL.md
~/.codebuddy/skills/flow-engine/SKILL.md
~/.claude/skills/flow-engine/SKILL.md`} label="skill-path" />
          <Text size="sm" mt="sm" mb="sm">Use this exact content for <Code>SKILL.md</Code>:</Text>
          <ConfigBlock code={CLAUDE_SKILL} label="SKILL.md" />
        </Paper>

        <Paper p="md" withBorder>
          <Title order={4} mb="sm">Using natural language</Title>
          <Text size="sm" mb="sm">Once connected, you can ask your AI client to:</Text>
          <List size="sm" spacing="xs">
            <List.Item>Create workflows: &quot;Build a workflow that fetches weather data every hour&quot;</List.Item>
            <List.Item>Modify workflows: &quot;Add an email notification step after the HTTP request&quot;</List.Item>
            <List.Item>Validate: &quot;Check if my workflow is correctly configured&quot;</List.Item>
            <List.Item>Execute: &quot;Run the workflow and show me the results&quot;</List.Item>
          </List>
        </Paper>

        <Paper p="md" withBorder>
          <Title order={4} mb="sm">Reviewing AI drafts</Title>
          <Text size="sm">
            When an AI client submits a draft, it appears in your workflow list with an
            <ThemeIcon variant="light" color="blue" size="xs" radius="sm" style={{ verticalAlign: 'middle' }}>AI</ThemeIcon> badge.
            Open it to enter Review Mode: inspect the proposed changes, run a dry run,
            then Confirm &amp; Activate or Reject with feedback.
          </Text>
        </Paper>
      </Stack>
    </div>
  );
}

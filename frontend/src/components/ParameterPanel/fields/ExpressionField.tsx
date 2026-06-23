import { useState, useRef, useEffect } from 'react';
import { Textarea, Text, Group, Stack, Code as MantineCode, ActionIcon, Tooltip, Paper, Divider, Box, Popover } from '@mantine/core';
import { HelpCircle, Code as CodeIcon, Braces } from 'lucide-react';
import { InfoTooltip } from './InfoTooltip.tsx';
import type { ParameterDefinition } from '../../../types/workflow.ts';

interface ExpressionFieldProps {
  definition: ParameterDefinition;
  value: unknown;
  onChange: (value: string) => void;
  error?: string;
}

import type { LucideIcon } from 'lucide-react';

// 从 definition.hintProperties 获取语言
function getScriptLanguage(definition: ParameterDefinition): string {
  const lang = definition.hintProperties?.language;
  if (typeof lang === 'string') return lang;
  if (typeof lang === 'number') return lang === 0 ? 'JavaScript' : 'Python';
  return 'JavaScript';
}

// 根据语言返回图标
function getLanguageIcon(language: string): LucideIcon {
  switch (language.toLowerCase()) {
    case 'javascript':
    case 'js':
      return Braces;
    case 'python':
    case 'py':
      return CodeIcon;
    default:
      return Braces;
  }
}

// 默认帮助内容
const DEFAULT_HELP_CONTENT = {
  title: 'Usage',
  sections: [
    {
      label: 'Simple expression (implicit return)',
      code: 'input.name',
    },
    {
      label: 'Arrow function',
      code: '({ input }) => input.name',
    },
    {
      label: 'Full function',
      code: 'function ({ input }) {\n  return input.name;\n}',
    },
  ],
  variables: [
    { name: '{ input }', description: 'Input data from previous node' },
  ],
};

export function ExpressionField({ definition, value, onChange, error }: ExpressionFieldProps) {
  const [isFullscreen, setIsFullscreen] = useState(false);
  const [helpOpened, setHelpOpened] = useState(false);
  const textareaRef = useRef<HTMLTextAreaElement>(null);

  const currentValue = String(value ?? '');
  const scriptLanguage = getScriptLanguage(definition);
  const LanguageIcon = getLanguageIcon(scriptLanguage);

  // 从 hintProperties 获取自定义帮助内容
  const helpContent = definition.hintProperties?.helpContent as typeof DEFAULT_HELP_CONTENT | undefined
    ?? DEFAULT_HELP_CONTENT;

  // Tab 缩进
  const handleKeyDown = (e: React.KeyboardEvent<HTMLTextAreaElement>) => {
    if (e.key === 'Tab') {
      e.preventDefault();
      const textarea = e.currentTarget;
      const start = textarea.selectionStart;
      const end = textarea.selectionEnd;
      const newValue = currentValue.substring(0, start) + '  ' + currentValue.substring(end);
      onChange(newValue);
      setTimeout(() => {
        textarea.selectionStart = textarea.selectionEnd = start + 2;
      }, 0);
    }
  };

  // 全屏模式下按 Escape 退出
  useEffect(() => {
    if (!isFullscreen) return;
    const handleEscape = (e: KeyboardEvent) => {
      if (e.key === 'Escape') setIsFullscreen(false);
    };
    window.addEventListener('keydown', handleEscape);
    return () => window.removeEventListener('keydown', handleEscape);
  }, [isFullscreen]);

  // 帮助面板内容
  const helpPanel = (
    <Stack gap="xs" p="xs">
      <Group gap={4}>
        <LanguageIcon size={14} />
        <Text size="xs" fw={600}>{helpContent.title} ({scriptLanguage})</Text>
      </Group>
      {helpContent.sections.map((section, i) => (
        <Stack key={i} gap={2}>
          <Text size="xs" fw={500} c="dimmed">{i + 1}. {section.label}</Text>
          <MantineCode block style={{ fontSize: 11 }}>
            {section.code}
          </MantineCode>
        </Stack>
      ))}
      <Divider />
      <Text size="xs" fw={600}>Available Variables</Text>
      {helpContent.variables.map((v, i) => (
        <Group key={i} gap={4}>
          <MantineCode style={{ fontSize: 11 }}>
            {v.name}
          </MantineCode>
          <Text size="xs" c="dimmed">- {v.description}</Text>
        </Group>
      ))}
    </Stack>
  );

  const editorContent = (
    <div>
      <Group gap={4} mb={4}>
        <Text size="xs" fw={400}>
          {definition.displayName}
          {definition.required && <span style={{ color: 'var(--mantine-color-error)' }}> *</span>}
        </Text>
        {definition.description && <InfoTooltip label={definition.description} />}
        <Box style={{ marginLeft: 'auto' }}>
          <Popover
            opened={helpOpened}
            onChange={setHelpOpened}
            position="bottom-end"
            width={320}
            shadow="sm"
          >
            <Popover.Target>
              <Tooltip label="Help">
                <ActionIcon size="xs" variant="subtle" onClick={() => setHelpOpened(!helpOpened)}>
                  <HelpCircle size={14} />
                </ActionIcon>
              </Tooltip>
            </Popover.Target>
            <Popover.Dropdown style={{ background: 'var(--mantine-color-body)', border: '1px solid var(--mantine-color-default-border)' }}>
              {helpPanel}
            </Popover.Dropdown>
          </Popover>
        </Box>
      </Group>

      <Textarea
        ref={textareaRef}
        error={error}
        value={currentValue}
        onChange={(e) => onChange(e.target.value)}
        onKeyDown={handleKeyDown}
        autosize
        minRows={isFullscreen ? 20 : 1}
        maxRows={isFullscreen ? 50 : 8}
        spellCheck={false}
        placeholder="input.name"
        rightSection={
          <Tooltip label={isFullscreen ? 'Exit Fullscreen' : `Fullscreen (${scriptLanguage})`}>
            <ActionIcon
              size="xs"
              variant="subtle"
              onClick={(e) => {
                e.stopPropagation();
                setIsFullscreen(!isFullscreen);
              }}
              mr={4}
            >
              <LanguageIcon size={14} />
            </ActionIcon>
          </Tooltip>
        }
        rightSectionWidth={30}
        styles={{
          input: {
            fontFamily: 'var(--mantine-font-family-monospace)',
            fontSize: 13,
            lineHeight: 1.5,
          }
        }}
      />
    </div>
  );

  // 全屏模式 - 左侧编辑器，右侧帮助
  if (isFullscreen) {
    return (
      <Box
        style={{
          position: 'fixed',
          top: 0,
          left: 0,
          right: 0,
          bottom: 0,
          zIndex: 1000,
          background: 'var(--mantine-color-body)',
          padding: 'var(--mantine-spacing-md)',
          display: 'flex',
          gap: 'var(--mantine-spacing-md)',
        }}
      >
        {/* 左侧：编辑器 */}
        <Box style={{ flex: 1, display: 'flex', flexDirection: 'column' }}>
          {editorContent}
        </Box>
        {/* 右侧：帮助面板 */}
        <Paper
          withBorder
          p="xs"
          style={{
            width: 320,
            flexShrink: 0,
            overflow: 'auto',
            background: 'var(--mantine-color-body)',
          }}
        >
          {helpPanel}
        </Paper>
      </Box>
    );
  }

  return editorContent;
}

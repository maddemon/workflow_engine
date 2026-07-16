import { useEffect, useRef, useState } from 'react';
import { Stack, Text, Box, Collapse, UnstyledButton, Group } from '@mantine/core';
import { Brain, ChevronRight, ChevronDown, Hash } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { CodeViewer } from '../ExecutionPanel/CodeViewer.tsx';
import type { LLMChunk, TokenUsage } from '../../types/agent-execution.ts';

interface LLMThinkingViewProps {
  chunks: LLMChunk[];
  systemPrompt?: string | null;
  tokenUsage?: TokenUsage | null;
  isStreaming?: boolean;
}

export function LLMThinkingView({ chunks, systemPrompt, tokenUsage, isStreaming }: LLMThinkingViewProps) {
  const { t } = useTranslation('execution');
  const [expanded, setExpanded] = useState(false);
  const [showSystemPrompt, setShowSystemPrompt] = useState(false);
  const contentRef = useRef<HTMLDivElement>(null);

  const content = chunks.map((c) => c.content).join('');

  useEffect(() => {
    if (isStreaming && contentRef.current) {
      contentRef.current.scrollTop = contentRef.current.scrollHeight;
    }
  }, [content, isStreaming]);

  return (
    <Box>
      <UnstyledButton
        onClick={() => setExpanded(!expanded)}
        w="100%"
        style={{
          borderRadius: 6,
          padding: '6px 8px',
          transition: 'background 0.15s ease',
        }}
        onMouseEnter={(e) => {
          (e.currentTarget as HTMLElement).style.background = 'var(--exec-hover)';
        }}
        onMouseLeave={(e) => {
          (e.currentTarget as HTMLElement).style.background = 'transparent';
        }}
      >
        <Group gap="xs" wrap="nowrap">
          <Box
            style={{
              width: 20,
              height: 20,
              borderRadius: 4,
              background: 'var(--mantine-color-violet-1)',
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
              flexShrink: 0,
            }}
          >
            <Brain size={12} color="var(--mantine-color-violet-6)" />
          </Box>
          <Text size="sm" fw={500} flex={1} ta="left">
            {t('llm.thinking')}
          </Text>
          {tokenUsage && (
            <Text size="xs" c="dimmed" style={{ flexShrink: 0 }}>
              {t('llm.tokens', { count: tokenUsage.totalTokens })}
            </Text>
          )}
          <Box style={{ color: 'var(--mantine-color-dimmed)', flexShrink: 0 }}>
            {expanded ? <ChevronDown size={14} /> : <ChevronRight size={14} />}
          </Box>
        </Group>
      </UnstyledButton>

      <Collapse expanded={expanded}>
        <Stack gap="xs" mt={4} ml={28}>
          {systemPrompt && (
            <Box>
              <UnstyledButton
                onClick={() => setShowSystemPrompt(!showSystemPrompt)}
                style={{
                  borderRadius: 4,
                  padding: '4px 6px',
                  transition: 'background 0.15s ease',
                }}
                onMouseEnter={(e) => {
                  (e.currentTarget as HTMLElement).style.background = 'var(--exec-hover)';
                }}
                onMouseLeave={(e) => {
                  (e.currentTarget as HTMLElement).style.background = 'transparent';
                }}
              >
                <Group gap="xs" wrap="nowrap">
                  <Text size="xs" c="dimmed" fw={500}>
                    {t('llm.systemPrompt')}
                  </Text>
                  <Box style={{ color: 'var(--mantine-color-dimmed)' }}>
                    {showSystemPrompt ? <ChevronDown size={12} /> : <ChevronRight size={12} />}
                  </Box>
                </Group>
              </UnstyledButton>
              <Collapse expanded={showSystemPrompt}>
                <Box mt={4}>
                  <CodeViewer label={t('llm.systemPrompt')} code={systemPrompt} language="text" maxHeight={120} />
                </Box>
              </Collapse>
            </Box>
          )}

          <Box
            ref={contentRef}
            style={{
              maxHeight: 200,
              overflow: 'auto',
              borderRadius: 6,
              border: '1px solid var(--exec-code-border)',
              background: 'var(--exec-code-bg)',
              padding: '8px 10px',
            }}
          >
            {content ? (
              <Text size="xs" style={{ lineHeight: 1.6, whiteSpace: 'pre-wrap', wordBreak: 'break-word' }}>
                {content}
                {isStreaming && (
                  <Box
                    component="span"
                    style={{
                      display: 'inline-block',
                      width: 6,
                      height: 12,
                      background: 'var(--mantine-color-violet-5)',
                      marginLeft: 2,
                      animation: 'blink 1s step-end infinite',
                    }}
                  />
                )}
              </Text>
            ) : (
              <Text size="xs" c="dimmed" ta="center" py="sm">
                {isStreaming ? t('llm.waitingForResponse') : t('llm.noThinkingContent')}
              </Text>
            )}
          </Box>

          {tokenUsage && (
            <Group gap="md" wrap="nowrap">
              <Group gap={4}>
                <Hash size={10} color="var(--mantine-color-dimmed)" />
                <Text size="xs" c="dimmed">
                  {t('llm.prompt', { count: tokenUsage.promptTokens })}
                </Text>
              </Group>
              <Group gap={4}>
                <Hash size={10} color="var(--mantine-color-dimmed)" />
                <Text size="xs" c="dimmed">
                  {t('llm.completion', { count: tokenUsage.completionTokens })}
                </Text>
              </Group>
              <Group gap={4}>
                <Hash size={10} color="var(--mantine-color-dimmed)" />
                <Text size="xs" c="dimmed">
                  {t('llm.total', { count: tokenUsage.totalTokens })}
                </Text>
              </Group>
            </Group>
          )}
        </Stack>
      </Collapse>
    </Box>
  );
}

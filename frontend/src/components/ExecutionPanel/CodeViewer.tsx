import { CodeHighlight } from "@mantine/code-highlight"
import { ActionIcon, Box, CopyButton, Group, Modal, Space, Text, Tooltip } from "@mantine/core"
import { Copy, Maximize2 } from "lucide-react"
import { useState } from "react"

interface CodeViewerProps {
  label: string
  code: string
  language?: string
  maxHeight?: number
}

export function CodeViewer({ label, code, language = "json", maxHeight = 120 }: CodeViewerProps) {
  const [modalOpen, setModalOpen] = useState(false)

  return (
    <Box>
      <Group justify="space-between" align="center" mb={4}>
        <Text size="xs" fw={600} c="dimmed" tt="uppercase" style={{ letterSpacing: "0.04em" }}>
          {label}
        </Text>
        <Group gap={2} wrap="nowrap">
          <CopyButton value={code}>
            {({ copied, copy }) => (
              <Tooltip label={copied ? "Copied" : "Copy"} position="left">
                <ActionIcon variant="subtle" color="gray" size="xs" onClick={copy}>
                  <Copy size={12} strokeWidth={1.5} />
                </ActionIcon>
              </Tooltip>
            )}
          </CopyButton>
          <Tooltip label="Full screen">
            <ActionIcon variant="subtle" color="gray" size="xs" onClick={() => setModalOpen(true)}>
              <Maximize2 size={12} strokeWidth={2} />
            </ActionIcon>
          </Tooltip>
        </Group>
      </Group>
      <Box
        style={{
          borderRadius: 6,
          border: "1px solid var(--exec-code-border)",
          background: "var(--exec-code-bg)",
          overflow: "hidden",
        }}
      >
        <div style={{ maxHeight, overflow: "auto" }}>
          <CodeHighlight
            code={code}
            language={language}
            withBorder={false}
            withCopyButton={false}
            style={{ maxWidth: "100%" }}
          />
        </div>
      </Box>
      <Modal
        opened={modalOpen}
        onClose={() => setModalOpen(false)}
        title={
          <Space>
            <Text size="sm" fw={600} c="dimmed">
              {label}
            </Text>
          </Space>
        }
        size="80%"
        padding={0}
      >
        <CodeHighlight
          code={code}
          language={language}
          withBorder={false}
          withCopyButton
          style={{ maxHeight: "65vh", overflow: "auto" }}
        />
      </Modal>
    </Box>
  )
}

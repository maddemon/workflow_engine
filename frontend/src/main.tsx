import './i18n' // i18n 初始化 — 必须放在最前面，确保所有 useTranslation 使用前已就绪
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { MantineProvider, ColorSchemeScript } from '@mantine/core'
import { Notifications } from '@mantine/notifications'
import { CodeHighlightAdapterProvider, createHighlightJsAdapter } from '@mantine/code-highlight'
import hljs from 'highlight.js'
import '@mantine/core/styles.css'
import '@mantine/notifications/styles.css'
import '@mantine/code-highlight/styles.css'
import 'highlight.js/styles/github.css'
import { theme } from './theme.ts'
import './index.css'
import App from './App.tsx'
import { setupGlobalErrorHandlers } from './utils/globalErrorHandler.ts'

setupGlobalErrorHandlers()

const highlightAdapter = createHighlightJsAdapter(hljs)

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <ColorSchemeScript defaultColorScheme="light" />
    <MantineProvider theme={theme} defaultColorScheme="light">
      <CodeHighlightAdapterProvider adapter={highlightAdapter}>
        <Notifications position="top-right" />
        <App />
      </CodeHighlightAdapterProvider>
    </MantineProvider>
  </StrictMode>,
)

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

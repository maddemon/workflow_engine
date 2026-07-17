import i18n, { initPromise } from './i18n' // i18n 初始化 — 放在最前面触发加载；首屏渲染由 initPromise 门控
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { I18nextProvider } from 'react-i18next'
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

// 等 i18n 所有命名空间加载完成后再挂载首屏，避免首屏访问尚未加载的命名空间
// 触发 "namespace xxx was not yet loaded" 警告（方案 A）。initPromise 在初始化失败时
// 仍会 resolve，因此不会造成永久白屏。
initPromise.then(() => {
  createRoot(document.getElementById('root')!).render(
    <StrictMode>
      <I18nextProvider i18n={i18n}>
        <ColorSchemeScript defaultColorScheme="light" />
        <MantineProvider theme={theme} defaultColorScheme="light">
          <CodeHighlightAdapterProvider adapter={highlightAdapter}>
            <Notifications position="top-right" />
            <App />
          </CodeHighlightAdapterProvider>
        </MantineProvider>
      </I18nextProvider>
    </StrictMode>,
  )
})

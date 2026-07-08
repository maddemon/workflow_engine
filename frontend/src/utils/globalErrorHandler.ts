import { notifications } from '@mantine/notifications';

/**
 * 注册全局未捕获错误兜底（R9）：避免未处理的 Promise 拒绝被静默吞掉，统一弹出通知。
 */
export function setupGlobalErrorHandlers(): void {
  window.addEventListener('unhandledrejection', (event) => {
    const reason = event.reason;
    const message = reason instanceof Error ? reason.message : String(reason);
    console.error('Unhandled promise rejection:', reason);
    notifications.show({
      title: 'Unexpected Error',
      message,
      color: 'red',
    });
  });

  window.addEventListener('error', (event) => {
    console.error('Uncaught error:', event.error ?? event.message);
  });
}

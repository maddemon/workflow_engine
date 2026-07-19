import { describe, it, expect, vi, beforeEach } from 'vitest';
import { setupGlobalErrorHandlers } from '../globalErrorHandler.ts';

vi.mock('@mantine/notifications', () => ({
  notifications: { show: vi.fn() },
}));

import { notifications } from '@mantine/notifications';

class UnhandledRejectionEvent extends Event {
  reason: unknown;
  constructor(reason: unknown) {
    super('unhandledrejection');
    this.reason = reason;
  }
}

describe('setupGlobalErrorHandlers', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('unhandledRejection_withError_showsNotification', () => {
    setupGlobalErrorHandlers();
    const error = new Error('promise rejected');
    window.dispatchEvent(new UnhandledRejectionEvent(error));

    expect(notifications.show).toHaveBeenCalledWith({
      title: 'Unexpected Error',
      message: 'promise rejected',
      color: 'red',
    });
  });

  it('unhandledRejection_withString_showsNotificationWithString', () => {
    setupGlobalErrorHandlers();
    window.dispatchEvent(new UnhandledRejectionEvent('plain reason'));

    expect(notifications.show).toHaveBeenCalledWith({
      title: 'Unexpected Error',
      message: 'plain reason',
      color: 'red',
    });
  });

  it('uncaughtError_logsToConsole', () => {
    const consoleSpy = vi.spyOn(console, 'error').mockImplementation(() => {});
    setupGlobalErrorHandlers();
    const error = new Error('boom');
    window.dispatchEvent(new ErrorEvent('error', { error, message: 'boom' }));

    expect(consoleSpy).toHaveBeenCalledWith('Uncaught error:', error);
    consoleSpy.mockRestore();
  });
});

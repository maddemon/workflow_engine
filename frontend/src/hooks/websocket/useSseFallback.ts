import { useCallback, useRef } from 'react';
import type { MutableRefObject } from 'react';
import type { WebSocketPushMessage, WebSocketStatus } from './messageHandlers.ts';

export interface UseSseFallbackOptions {
  getSseUrl: (executionId: string) => string;
  processMessage: (message: WebSocketPushMessage) => void;
  lastSequenceRef: MutableRefObject<number>;
  setLastSequence: (n: number) => void;
  setStatus: (status: WebSocketStatus) => void;
}

export function useSseFallback(options: UseSseFallbackOptions) {
  const { getSseUrl, processMessage, lastSequenceRef, setLastSequence, setStatus } = options;
  const eventSourceRef = useRef<EventSource | null>(null);

  const trySseFallback = useCallback((executionId: string) => {
    eventSourceRef.current?.close();
    eventSourceRef.current = null;

    const eventSource = new EventSource(getSseUrl(executionId));
    setStatus('connected');

    eventSource.onmessage = (event) => {
      try {
        const message = JSON.parse(event.data) as WebSocketPushMessage;
        lastSequenceRef.current = message.sequence;
        setLastSequence(message.sequence);
        processMessage(message);
      } catch {
        console.error('Failed to parse SSE message');
      }
    };

    eventSource.onerror = () => {
      setStatus('error');
      eventSource.close();
      eventSourceRef.current = null;
    };

    eventSourceRef.current = eventSource;
  }, [getSseUrl, processMessage]);

  const closeSse = useCallback(() => {
    if (eventSourceRef.current) {
      eventSourceRef.current.close();
      eventSourceRef.current = null;
    }
  }, []);

  return { trySseFallback, closeSse };
}

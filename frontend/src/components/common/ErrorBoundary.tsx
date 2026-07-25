import { Component, type ErrorInfo, type ReactNode } from 'react';
import { Alert, Button, Stack, Text } from '@mantine/core';

interface ErrorBoundaryProps {
  children: ReactNode;
  fallbackTitle?: string;
}

interface ErrorBoundaryState {
  hasError: boolean;
  error: Error | null;
}

/**
 * Q-3：通用错误边界。包裹高风险区域（画布、参数面板、执行面板），
 * 避免单个组件的渲染异常导致整页白屏。仅记录错误，不向上抛出。
 */
export class ErrorBoundary extends Component<ErrorBoundaryProps, ErrorBoundaryState> {
  constructor(props: ErrorBoundaryProps) {
    super(props);
    this.state = { hasError: false, error: null };
  }

  static getDerivedStateFromError(error: Error): ErrorBoundaryState {
    return { hasError: true, error };
  }

  componentDidCatch(error: Error, info: ErrorInfo): void {
    // 仅记录，不向上抛出，避免整页崩溃。
    console.error('ErrorBoundary caught an error:', error, info);
  }

  private handleReset = (): void => {
    this.setState({ hasError: false, error: null });
  };

  render(): ReactNode {
    if (this.state.hasError) {
      return (
        <Stack p="md" gap="xs">
          <Alert color="red" title={this.props.fallbackTitle ?? '组件加载失败'} withCloseButton onClose={this.handleReset}>
            <Text size="sm">{this.state.error?.message ?? '发生未知错误'}</Text>
          </Alert>
          <Button variant="light" size="xs" onClick={this.handleReset}>重试</Button>
        </Stack>
      );
    }

    return this.props.children;
  }
}

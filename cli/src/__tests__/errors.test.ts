import { describe, expect, it } from 'vitest';
import { CLIError, ErrorCode, ExitCode } from '../errors.ts';

describe('errors', () => {
  it('ExitCode - contains required values', () => {
    expect(ExitCode.Success).toBe(0);
    expect(ExitCode.BusinessFailure).toBe(1);
    expect(ExitCode.InvocationError).toBe(2);
    expect(ExitCode.UserInterrupted).toBe(130);
  });

  it('ErrorCode - contains required values', () => {
    expect(ErrorCode.AuthRequired).toBe('AUTH_REQUIRED');
    expect(ErrorCode.ValidationError).toBe('VALIDATION_ERROR');
    expect(ErrorCode.NetworkError).toBe('NETWORK_ERROR');
  });

  it('CLIError - carries code, exitCode and details', () => {
    const error = new CLIError('login failed', ErrorCode.AuthRequired, ExitCode.BusinessFailure, {
      retry: false,
    });
    expect(error.message).toBe('login failed');
    expect(error.code).toBe(ErrorCode.AuthRequired);
    expect(error.exitCode).toBe(ExitCode.BusinessFailure);
    expect(error.details).toEqual({ retry: false });
    expect(error.name).toBe('CLIError');
  });

  it('CLIError - defaults exitCode to BusinessFailure', () => {
    const error = new CLIError('bad input', ErrorCode.ValidationError);
    expect(error.exitCode).toBe(ExitCode.BusinessFailure);
    expect(error.details).toBeUndefined();
  });
});

export enum ExitCode {
  Success = 0,
  BusinessFailure = 1,
  InvocationError = 2,
  UserInterrupted = 130,
}

export enum ErrorCode {
  UnexpectedError = 'UNEXPECTED_ERROR',
  ConfigReadError = 'CONFIG_READ_ERROR',
  ConfigWriteError = 'CONFIG_WRITE_ERROR',
  ConfigNotFound = 'CONFIG_NOT_FOUND',
  InvalidConfig = 'INVALID_CONFIG',
  AuthRequired = 'AUTH_REQUIRED',
  Forbidden = 'FORBIDDEN',
  NotFound = 'NOT_FOUND',
  ValidationError = 'VALIDATION_ERROR',
  Conflict = 'CONFLICT',
  NetworkError = 'NETWORK_ERROR',
  ServerError = 'SERVER_ERROR',
  ApiError = 'API_ERROR',
  UserInterrupted = 'USER_INTERRUPTED',
}

export class CLIError extends Error {
  constructor(
    message: string,
    public readonly code: ErrorCode,
    public readonly exitCode: ExitCode = ExitCode.BusinessFailure,
    public readonly details?: unknown,
  ) {
    super(message);
    this.name = 'CLIError';
  }
}

// Token is now managed via HttpOnly cookie (`fe_auth`) set by the backend.
// This module is kept as a no-op for backward compatibility but can be removed.
let currentToken: string | null = null;

export const tokenStore = {
  getToken: (): string | null => currentToken,
  setToken: (_token: string | null): void => {
    currentToken = _token;
  },
  clear: (): void => {
    currentToken = null;
  },
};

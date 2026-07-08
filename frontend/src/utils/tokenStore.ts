// 内存令牌存储：避免在 XSS 敏感的 localStorage 中持久化 JWT（L1）。
// 注意：真正的 HttpOnly Cookie 需由后端在登录时下发；此处先移除 localStorage 暴露面，
// 并集中令牌读取（A10），便于后续后端下发 HttpOnly Cookie 时无缝切换。
let currentToken: string | null = null;

export const tokenStore = {
  getToken: (): string | null => currentToken,
  setToken: (token: string | null): void => {
    currentToken = token;
  },
  clear: (): void => {
    currentToken = null;
  },
};

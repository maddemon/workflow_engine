import react from "@vitejs/plugin-react"
import { defineConfig } from "vitest/config"

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    port: 4000,
    proxy: {
      "/api": {
        target: "http://localhost:8001",
        changeOrigin: true,
      },
    },
  },
  build: {
    outDir: "../backend/FlowEngine.Host/wwwroot",
  },
  test: {
    environment: "jsdom",
    globals: true,
    setupFiles: ["./src/test-setup.ts"],
    coverage: {
      provider: "v8",
      reportsDirectory: "./coverage",
      include: ["src/**/*.{ts,tsx}"],
      thresholds: {
        lines: 65,
        // 分支覆盖率门禁：与后端 _check_coverage.ps1 的 minBranch=0.55 思路一致，
        // 前端略放宽至 50 以匹配其当前 53% 左右的实际水平，避免无谓跌破。
        branches: 50,
      },
    },
  },
})

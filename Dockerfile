# ── Backend build ─────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend-build
WORKDIR /src
COPY backend/ ./backend/
COPY plugins/ ./plugins/
RUN dotnet publish backend/FlowEngine.Host/FlowEngine.Host.csproj -c Release -o /app/publish

# ── Frontend build ───────────────────────────────────────────
FROM node:20-alpine AS frontend-build
WORKDIR /web
COPY frontend/package*.json ./
RUN npm ci
COPY frontend/ ./
RUN npm run build

# ── Runtime ──────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# 后端发布物（含插件 DLL，见 Host.csproj 的 CopyPluginsOnPublish target）
COPY --from=backend-build /app/publish ./

# 前端静态资源（由 ASP.NET 的 UseStaticFiles + MapFallbackToFile 提供）
COPY --from=frontend-build /web/dist ./wwwroot

EXPOSE 8080

# 安全提示（对应审查 H1）：
# - 必须通过环境变量/密钥注入 Jwt:Secret（至少 32 字节），禁止文档化默认口令。
# - 首次启动设置 FLOWENGINE_ADMIN_PASSWORD（或 Setup:AdminPassword）创建管理员，缺失则启动失败。
ENTRYPOINT ["dotnet", "FlowEngine.dll"]

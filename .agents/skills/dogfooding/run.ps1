#!/usr/bin/env pwsh
<#
.SYNOPSIS
  启动 Dogfooding 工作流自动化：生成场景 → MCP 构建 → 分析 → 改进
.DESCRIPTION
  自动确保 Flow Engine Host 在 :8001 运行，然后执行 orchestrator 一轮完整 pipeline。
  报告输出在 docs/superpowers/dogfooding/runs/。
#>

$ErrorActionPreference = 'Stop'
$ScriptDir = Split-Path -Parent $PSCommandPath
Set-Location $ScriptDir

# ── 1. 环境变量 ──
# 从 opencode.json 读取 API key（或直接设置）
$opencodeConfig = Join-Path (Resolve-Path "$ScriptDir/../../../..") 'opencode.json'
if (Test-Path $opencodeConfig) {
    $json = Get-Content $opencodeConfig -Raw | ConvertFrom-Json
    $key = $json.mcpServers.PSObject.Properties.Where({ $_.Name -like '*flow*' }).Value.defaultHeaders.'x-api-key'
    if ($key) { $env:FLOWENGINE_API_KEY = $key }
}
if (-not $env:FLOWENGINE_API_KEY) {
    $env:FLOWENGINE_API_KEY = 'fe_VPY2kMdr74jIFVz4Hr6f834AVJjyN3kKpXzBAvB9odU'
}

# ── 2. 确保 Flow Engine Host 在线 ──
$portCheck = netstat -ano | Select-String ':8001.*LISTENING'
if (-not $portCheck) {
    Write-Host "[Dogfooding] Host 未运行，启动 FlowEngine.Host ..." -ForegroundColor Yellow
    $backendDir = Resolve-Path "$ScriptDir/../../../../backend"
    $logFile = "$env:TEMP\flowengine-host.log"
    $proc = Start-Process -NoNewWindow -PassThru -FilePath dotnet `
        -ArgumentList "run", "--project", "FlowEngine.Host" `
        -WorkingDirectory $backendDir `
        -RedirectStandardOutput $logFile -RedirectStandardError "${logFile}.err"

    # 等待最多 60s 直到 8001 就绪
    $ready = $false
    for ($i = 0; $i -lt 60; $i++) {
        Start-Sleep -Seconds 1
        if (netstat -ano | Select-String ':8001.*LISTENING') {
            $ready = $true
            break
        }
    }
    if (-not $ready) {
        Write-Host "[Dogfooding] Host 启动超时，查看日志:" -ForegroundColor Red
        Get-Content $logFile -Tail 10
        exit 1
    }
    Write-Host "[Dogfooding] FlowEngine.Host 已就绪 (PID $($proc.Id))" -ForegroundColor Green
} else {
    Write-Host "[Dogfooding] FlowEngine.Host 已在 :8001 运行" -ForegroundColor Green
}

# ── 3. 运行一轮 ──
Write-Host "[Dogfooding] 启动 orchestrator ..." -ForegroundColor Cyan
npx tsx src/orchestrator.ts

# ── 4. 输出结果路径 ──
$reportDir = Join-Path (Resolve-Path $ScriptDir) '../../docs/superpowers/dogfooding/runs'
if (Test-Path $reportDir) {
    $latest = Get-ChildItem $reportDir | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($latest) {
        Write-Host "`n[Dogfooding] 最新报告: $($latest.FullName)" -ForegroundColor Green
    }
}

# 任务：验证与收尾

## 目标

全量运行前后端测试并产出覆盖率报告，对照 `plan-unit-test-coverage.md` 目标确认达成；未达标则回流对应 Phase 补测。

## 待完成项

> **第一步（Issue #8）**：先实测基线（8.1 → 8.2）并把真实数字回填计划 §1，再判断目标达成度；若实测基线已接近目标，及时调整各 Phase 投入，避免过度测试。

- [ ] **8.1 后端全量测试（生成各项目覆盖率）**
  ```bash
  dotnet test FlowEngine.sln --collect:"XPlat Code Coverage" --results-directory TestResults
  ```
  > 注意：`dotnet test` 会为每个测试项目各生成一份 `coverage.cobertura.xml`，须**全部合并**后再统计，单取一份不能代表后端整体（Issue #1）。

- [ ] **8.2 合并覆盖率并解析后端整体（Issue #1）**
  用 ReportGenerator 合并所有 `coverage.cobertura.xml`（按类/行去重，得到真实后端整体；简单平均会因 Core 等被多项目重复计入而失真）：
  ```powershell
  # 0) 收集所有覆盖率文件
  $files = Get-ChildItem -Path TestResults -Filter "coverage.cobertura.xml" -Recurse
  if (-not $files) { Write-Error "未找到 coverage.cobertura.xml，请先运行 8.1"; exit 1 }

  # 1) 确保 ReportGenerator 可用（一次性安装全局工具）
  if (-not (Get-Command reportgenerator -ErrorAction SilentlyContinue)) {
      Write-Host "ReportGenerator 未安装，尝试安装全局工具（一次性）..."
      dotnet tool install --global dotnet-reportgenerator-globaltool
  }

  # 2) 合并为单一 Cobertura 并读取真实整体
  reportgenerator "-reports:$($files.FullName -join ';')" "-targetdir:TestResults/Merged" "-reporttypes:Cobertura"
  $merged = "TestResults/Merged/Cobertura.xml"
  if (Test-Path $merged) {
      [xml]$m = Get-Content $merged
      $line   = [math]::Round([double]$m.coverage.'line-rate' * 100, 1)
      $branch = [math]::Round([double]$m.coverage.'branch-rate' * 100, 1)
      Write-Host "Backend Overall (merged): Line $line% | Branch $branch%  (branch 仅参考)"
      $m.coverage.packages.package | ForEach-Object {
          $pkg  = $_.name -replace 'FlowEngine\.', ''
          $rate = [math]::Round([double]$_.'line-rate' * 100, 1)
          Write-Host "  $pkg : $rate%"
      }
  } else {
      Write-Host "合并失败，回退展示各项目 line-rate（注意跨项目重复计数，仅作参考）"
      $files | ForEach-Object {
          [xml]$x = Get-Content $_.FullName
          $lr = [math]::Round([double]$x.coverage.'line-rate' * 100, 1)
          Write-Host "  $($_.Directory.Parent.Name) : $lr%"
      }
  }
  ```

- [ ] **8.3 回填实测基线（Issue #8 第一步）**
  - 将 8.2 实测的**后端整体 / 各模块**数字回填 `plan-unit-test-coverage.md` §1 概述，替换原 54% / 各模块 0% 类数等**未经验证**数字。
  - 若实测基线已接近目标（如后端整体已 ≥ 65%），评估是否仍需全部 Phase，必要时削减投入。

- [ ] **8.4 前端覆盖率前置检查 + 运行（Issue #4）**
  - 前置检查：`frontend/vite.config.ts` 的 `test` 块须含 `coverage: { provider: "v8", ... }`（依赖 `@vitest/coverage-v8`，已在 `package.json` 安装，本次计划已补该配置）。若 `--coverage` 报 "Missing coverage provider"，先补该配置再运行。
  - 运行：
  ```bash
  cd frontend && npx vitest run --coverage
  ```

- [x] **8.5 对照目标表（2026-07-17 实测，用户决策冲 75%+）**
  | 模块 | 基线（实测回填） | 目标（75%+ 标准） | 状态 |
  |------|------|------|------|
  | 后端 Application | 76.8% | 82%+ | 真实缺口 ~5pt |
  | 后端 Core | 52.5% | 65%+ | 真实缺口 ~12.5pt |
  | 后端 Runtime | 65.0% | 75%+ | 真实缺口 ~10pt |
  | 后端 Plugins.Standard | 58.1% | 70%+ | 真实缺口 ~12pt |
  | 后端 Infrastructure | 41.7% | 65%+ | 真实缺口 ~23pt |
  | 后端 Host | 58.9% | 75%+ | 真实缺口 ~16pt |
  | 后端 Resources | 57.5% | 按需补 | 真实缺口 |
  | **后端整体** | **68.9%（加权）** | **75%+** | 差 ~6pt |
  | 前端 Lines | 16.43% | 65%+ | **最大缺口 ~49pt** |
  - 口径：**Cobertura line-rate**（branch-rate 仅参考）；前端取 v8 `% Lines`。
  - 实测方式：后端 `dotnet test FlowEngine.sln --no-restore --collect:"XPlat Code Coverage"`（离线，依赖缓存 `project.assets.json`），7 份 `coverage.cobertura.xml` 按各程序集最优取 MAX 并加权汇总（ReportGenerator 离线不可用，用 `TestResults/_parse_cov.ps1` 兜底）；前端 `npx vitest run --coverage`（v8，19 文件 / 118 用例全绿）。

- [ ] **8.6 不达标回流**：任一模块未达目标，回流对应 `task-00X-*.md` 补测，直至整体达标。

- [ ] **8.7 合规 grep 校验（补充验收项）**
  ```bash
  # 全仓库禁用 FluentAssertions
  grep -rln "FluentAssertions" tests/ frontend/src/ && echo "FAIL: 发现 FluentAssertions" || echo "OK: 无 FluentAssertions"
  # 非 Host 测试项目禁用 Moq
  grep -rln "using Moq" tests/FlowEngine.Core.Tests tests/FlowEngine.Application.Tests tests/FlowEngine.Runtime.Tests tests/FlowEngine.Infrastructure.Tests && echo "FAIL: 非 Host 项目使用了 Moq" || echo "OK: Moq 仅限 Host"
  # 前端禁用 as any
  grep -rn "as any" frontend/src/ && echo "FAIL: 前端出现 as any" || echo "OK: 前端无 as any"
  ```

- [ ] **8.8 提交（不主动推送，中性 message）（Issue #6）**
  ```bash
  git add tests/ frontend/src/ docs/plans/
  git commit -m "test: 补充前后端单元测试，提升覆盖率"
  ```

## 完成标准

- 后端整体行覆盖率（**Cobertura line-rate**，合并后）≥ 75%（用户 2026-07-17 决策上调自 70%），前端 ≥ 65%（以 8.2 / 8.4 实测为准）。
- 所有 Task 001–007 的测试项目 `dotnet test` / `npx vitest run` 全绿，且 `dotnet build` / 前端 `npm run build` + `npm run typecheck` 无错误。
- 8.7 三项 grep 校验全部 OK。
- 覆盖率报告已产出，且计划 §1 基线数字已回填实测值。

## 完成状态

- [x] 8.1 后端全量测试（离线 --no-restore，7 份覆盖率）
- [x] 8.2 合并/解析后端整体（ReportGenerator 不可用，用 _parse_cov.ps1 兜底，得 68.9%）
- [x] 8.3 回填实测基线至 plan §1.1（原 54%/50.4% 臆测值已替换）
- [x] 8.4 前端覆盖率运行（coverage provider 已补，得 16.43% 行）
- [x] 8.5 对照目标表（见上，已实测回填）
- [ ] 8.6 不达标回流（待计划 Phase 1–7 执行后回流）
- [ ] 8.7 合规 grep 校验（待执行）
- [ ] 8.8 提交（待执行，中性 message）

## 主要修改记录

- 重写自 `plan-unit-test-coverage.md`：覆盖率脚本改为 ReportGenerator 合并（去重）得真实后端整体（Issue #1）；前端增加 coverage provider 前置检查（Issue #4）；提交信息改为中性（Issue #6）；基线实测列为第一步并回填（Issue #8）；新增 grep 合规校验与 line-rate 口径说明（补充验收项）。

# FlowEngine MVP 验收测试数据

> 使用前确保后端已启动：`dotnet run --project backend/FlowEngine.Host`
> 默认地址：`http://localhost:5000`

---

## 1. 查看可用节点类型

```powershell
Invoke-RestMethod http://localhost:5000/api/v1/node-types | ConvertTo-Json -Depth 5
```

预期返回：httpRequest、if、script、switch 四种类型。

---

## 2. 创建凭据（加密存储测试）

```powershell
$cred = Invoke-RestMethod -Method POST http://localhost:5000/api/v1/credentials -Body '{
  "name": "My API Key",
  "type": "apiKey",
  "fields": {
    "apiKey": "sk-test-1234567890abcdef"
  }
}' -ContentType "application/json"

Write-Host "凭据 ID: $($cred.Id)"
```

验证加密：再 GET 一次，响应中不应包含明文 `sk-test-...`。

```powershell
Invoke-RestMethod "http://localhost:5000/api/v1/credentials/$($cred.Id)" | ConvertTo-Json -Depth 5
```

---

## 3. 创建线性工作流：HTTP → If → Code

这是一个完整的工作流：
- **HTTP Request** 节点调用 httpbin.org GET 请求
- **If** 节点判断状态码是否为 200
- **JavaScript** 节点处理结果

```powershell
$httpNodeId = [Guid]::NewGuid()
$ifNodeId = [Guid]::NewGuid()
$codeNodeId = [Guid]::NewGuid()

$workflow = Invoke-RestMethod -Method POST http://localhost:5000/api/v1/workflows -Body (@{
  name = "HTTP → If → Code 测试工作流"
  createdBy = "mvp-test"
  nodes = @(
    @{
      id = $httpNodeId.ToString()
      typeName = "httpRequest"
      name = "HTTP Request"
      parameters = @{
        method = "Get"
        url = "https://httpbin.org/get"
      }
      ports = @(
        @{ name = "input"; direction = "Input"; type = "Main" }
        @{ name = "output"; direction = "Output"; type = "Main" }
      )
      positionX = 100
      positionY = 200
      isEntry = $true
      errorStrategy = "StopWorkflow"
    },
    @{
      id = $ifNodeId.ToString()
      typeName = "if"
      name = "If Status OK"
      parameters = @{
        condition = "{{ input.statusCode }} == 200"
      }
      ports = @(
        @{ name = "input"; direction = "Input"; type = "Main" }
        @{ name = "true"; direction = "Output"; type = "Main" }
        @{ name = "false"; direction = "Output"; type = "Main" }
      )
      positionX = 400
      positionY = 200
      isEntry = $false
      errorStrategy = "StopWorkflow"
    },
    @{
      id = $codeNodeId.ToString()
      typeName = "script"
      name = "Process Result"
      parameters = @{
        code = "return { message: 'Success!', statusCode: input.statusCode, url: input.body?.url || 'unknown' }"
      }
      ports = @(
        @{ name = "input"; direction = "Input"; type = "Main" }
        @{ name = "output"; direction = "Output"; type = "Main" }
      )
      positionX = 700
      positionY = 150
      isEntry = $false
      errorStrategy = "StopWorkflow"
    }
  )
  connections = @(
    @{
      id = "c1"
      sourceNodeId = $httpNodeId.ToString()
      sourcePortName = "output"
      targetNodeId = $ifNodeId.ToString()
      targetPortName = "input"
    },
    @{
      id = "c2"
      sourceNodeId = $ifNodeId.ToString()
      sourcePortName = "true"
      targetNodeId = $codeNodeId.ToString()
      targetPortName = "input"
    }
  )
} | ConvertTo-Json -Depth 10) -ContentType "application/json"

Write-Host "工作流 ID: $($workflow.Id)"
Write-Host "版本: $($workflow.Version)"
```

---

## 4. 验证保存和加载

```powershell
# 重新加载
$loaded = Invoke-RestMethod "http://localhost:5000/api/v1/workflows/$($workflow.Id)"
Write-Host "加载版本: $($loaded.Version)"
Write-Host "节点数: $($loaded.nodes.Count)"
Write-Host "连接数: $($loaded.connections.Count)"

# 更新（版本应递增）
$updated = Invoke-RestMethod -Method PUT "http://localhost:5000/api/v1/workflows/$($workflow.Id)" -Body (@{
  name = "HTTP → If → Code 测试工作流 (v2)"
  isActive = $true
  nodes = $loaded.nodes
  connections = $loaded.connections
} | ConvertTo-Json -Depth 10) -ContentType "application/json"

Write-Host "更新后版本: $($updated.Version)"
```

---

## 5. 执行工作流

```powershell
$execution = Invoke-RestMethod -Method POST "http://localhost:5000/api/v1/workflows/$($workflow.Id)/execute"
Write-Host "执行 ID: $($execution.Id)"
Write-Host "初始状态: $($execution.Status)"

# 轮询等待完成（最多 30 秒）
$maxWait = 30
$waited = 0
do {
  Start-Sleep -Seconds 2
  $waited += 2
  $exec = Invoke-RestMethod "http://localhost:5000/api/v1/executions/$($execution.Id)"
  Write-Host "[$waited s] 状态: $($exec.Status)"
} while ($exec.Status -notin @("Completed", "Failed", "Cancelled") -and $waited -lt $maxWait)

# 显示结果
Write-Host "`n=== 执行结果 ==="
Write-Host "最终状态: $($exec.Status)"
Write-Host "节点执行记录数: $($exec.nodeRecords.Count)"

foreach ($record in $exec.nodeRecords) {
  Write-Host "`n--- 节点: $($record.nodeDefinitionId) ---"
  Write-Host "  状态: $($record.status)"
  Write-Host "  输出: $($record.output | ConvertTo-Json -Compress)"
}
```

---

## 6. 表达式求值测试

创建一个使用 `{{ input.xxx }}` 表达式的工作流：

```powershell
$exprHttpId = [Guid]::NewGuid()
$exprCodeId = [Guid]::NewGuid()

$exprWorkflow = Invoke-RestMethod -Method POST http://localhost:5000/api/v1/workflows -Body (@{
  name = "表达式求值测试"
  createdBy = "mvp-test"
  nodes = @(
    @{
      id = $exprHttpId.ToString()
      typeName = "httpRequest"
      name = "Fetch Data"
      parameters = @{
        method = "Get"
        url = "https://httpbin.org/get"
      }
      ports = @(
        @{ name = "input"; direction = "Input"; type = "Main" }
        @{ name = "output"; direction = "Output"; type = "Main" }
      )
      positionX = 100
      positionY = 200
      isEntry = $true
      errorStrategy = "StopWorkflow"
    },
    @{
      id = $exprCodeId.ToString()
      typeName = "script"
      name = "Use Expression"
      parameters = @{
        code = "return { url: input.body?.url, status: input.statusCode, extracted: 'Status was ' + input.statusCode }"
      }
      ports = @(
        @{ name = "input"; direction = "Input"; type = "Main" }
        @{ name = "output"; direction = "Output"; type = "Main" }
      )
      positionX = 400
      positionY = 200
      isEntry = $false
      errorStrategy = "StopWorkflow"
    }
  )
  connections = @(
    @{
      id = "c1"
      sourceNodeId = $exprHttpId.ToString()
      sourcePortName = "output"
      targetNodeId = $exprCodeId.ToString()
      targetPortName = "input"
    }
  )
} | ConvertTo-Json -Depth 10) -ContentType "application/json"

Write-Host "表达式工作流 ID: $($exprWorkflow.Id)"

# 执行
$exprExec = Invoke-RestMethod -Method POST "http://localhost:5000/api/v1/workflows/$($exprWorkflow.Id)/execute"
Start-Sleep -Seconds 5
$exprResult = Invoke-RestMethod "http://localhost:5000/api/v1/executions/$($exprExec.Id)"
Write-Host "执行状态: $($exprResult.Status)"
Write-Host "节点记录: $($exprResult.nodeRecords.Count)"
foreach ($r in $exprResult.nodeRecords) {
  Write-Host "  节点 $($r.nodeDefinitionId): $($r.status) | 输出: $($r.output | ConvertTo-Json -Compress)"
}
```

---

## 7. 错误重试测试

创建一个会失败的 HTTP 节点，配置重试策略：

```powershell
$retryHttpId = [Guid]::NewGuid()
$retryCodeId = [Guid]::NewGuid()

$retryWorkflow = Invoke-RestMethod -Method POST http://localhost:5000/api/v1/workflows -Body (@{
  name = "错误重试测试"
  createdBy = "mvp-test"
  nodes = @(
    @{
      id = $retryHttpId.ToString()
      typeName = "httpRequest"
      name = "Failing HTTP"
      parameters = @{
        method = "Get"
        url = "https://httpbin.org/status/500"
      }
      ports = @(
        @{ name = "input"; direction = "Input"; type = "Main" }
        @{ name = "output"; direction = "Output"; type = "Main" }
      )
      positionX = 100
      positionY = 200
      isEntry = $true
      errorStrategy = "Continue"
      retryPolicy = @{
        maxRetries = 2
        baseDelayMs = 1000
      }
    },
    @{
      id = $retryCodeId.ToString()
      typeName = "script"
      name = "After Retry"
      parameters = @{
        code = "return { message: 'Completed after error handling' }"
      }
      ports = @(
        @{ name = "input"; direction = "Input"; type = "Main" }
        @{ name = "output"; direction = "Output"; type = "Main" }
      )
      positionX = 400
      positionY = 200
      isEntry = $false
      errorStrategy = "StopWorkflow"
    }
  )
  connections = @(
    @{
      id = "c1"
      sourceNodeId = $retryHttpId.ToString()
      sourcePortName = "output"
      targetNodeId = $retryCodeId.ToString()
      targetPortName = "input"
    }
  )
} | ConvertTo-Json -Depth 10) -ContentType "application/json"

Write-Host "重试工作流 ID: $($retryWorkflow.Id)"

$retryExec = Invoke-RestMethod -Method POST "http://localhost:5000/api/v1/workflows/$($retryWorkflow.Id)/execute"
Start-Sleep -Seconds 8
$retryResult = Invoke-RestMethod "http://localhost:5000/api/v1/executions/$($retryExec.Id)"
Write-Host "执行状态: $($retryResult.Status)"
foreach ($r in $retryResult.nodeRecords) {
  Write-Host "  节点: $($r.status) | 输出: $($r.output | ConvertTo-Json -Compress)"
}
```

---

## 8. 完整 E2E：HTTP → If → Code（带凭据）

使用之前创建的凭据 ID：

```powershell
# 替换为实际的凭据 ID
$credId = $cred.Id

$e2eHttpId = [Guid]::NewGuid()
$e2eIfId = [Guid]::NewGuid()
$e2eCodeId = [Guid]::NewGuid()

$e2eWorkflow = Invoke-RestMethod -Method POST http://localhost:5000/api/v1/workflows -Body (@{
  name = "E2E: HTTP + 凭据 → If → Code"
  createdBy = "mvp-test"
  nodes = @(
    @{
      id = $e2eHttpId.ToString()
      typeName = "httpRequest"
      name = "API Call with Auth"
      parameters = @{
        method = "Get"
        url = "https://httpbin.org/get"
        apiCredential = $credId
      }
      ports = @(
        @{ name = "input"; direction = "Input"; type = "Main" }
        @{ name = "output"; direction = "Output"; type = "Main" }
      )
      positionX = 100
      positionY = 200
      isEntry = $true
      errorStrategy = "StopWorkflow"
    },
    @{
      id = $e2eIfId.ToString()
      typeName = "if"
      name = "Check Status"
      parameters = @{
        condition = "{{ input.statusCode }} == 200"
      }
      ports = @(
        @{ name = "input"; direction = "Input"; type = "Main" }
        @{ name = "true"; direction = "Output"; type = "Main" }
        @{ name = "false"; direction = "Output"; type = "Main" }
      )
      positionX = 400
      positionY = 200
      isEntry = $false
      errorStrategy = "StopWorkflow"
    },
    @{
      id = $e2eCodeId.ToString()
      typeName = "script"
      name = "Transform Data"
      parameters = @{
        code = @"
return {
  success: true,
  statusCode: input.statusCode,
  message: 'API call succeeded',
  data: input.body
}
"@
      }
      ports = @(
        @{ name = "input"; direction = "Input"; type = "Main" }
        @{ name = "output"; direction = "Output"; type = "Main" }
      )
      positionX = 700
      positionY = 150
      isEntry = $false
      errorStrategy = "StopWorkflow"
    }
  )
  connections = @(
    @{
      id = "c1"
      sourceNodeId = $e2eHttpId.ToString()
      sourcePortName = "output"
      targetNodeId = $e2eIfId.ToString()
      targetPortName = "input"
    },
    @{
      id = "c2"
      sourceNodeId = $e2eIfId.ToString()
      sourcePortName = "true"
      targetNodeId = $e2eCodeId.ToString()
      targetPortName = "input"
    }
  )
} | ConvertTo-Json -Depth 10) -ContentType "application/json"

Write-Host "E2E 工作流 ID: $($e2eWorkflow.Id)"

$e2eExec = Invoke-RestMethod -Method POST "http://localhost:5000/api/v1/workflows/$($e2eWorkflow.Id)/execute"
Start-Sleep -Seconds 5
$e2eResult = Invoke-RestMethod "http://localhost:5000/api/v1/executions/$($e2eExec.Id)"
Write-Host "执行状态: $($e2eResult.Status)"
foreach ($r in $e2eResult.nodeRecords) {
  Write-Host "  节点: $($r.status) | 输出: $($r.output | ConvertTo-Json -Compress -Depth 3)"
}
```

---

## 快速验收检查清单

| # | 验收项 | 测试命令 | 预期结果 |
|---|--------|----------|----------|
| 1 | 节点类型列表 | 步骤 1 | 返回 4 种类型 |
| 2 | 凭据加密存储 | 步骤 2 | GET 不返回明文 |
| 3 | 工作流保存 | 步骤 3 | 返回工作流 ID |
| 4 | 工作流加载 | 步骤 4 | 节点和连接完整 |
| 5 | 版本递增 | 步骤 4 | Version 从 1 → 2 |
| 6 | 执行线性工作流 | 步骤 5 | 状态 Completed |
| 7 | 表达式求值 | 步骤 6 | `{{ input.xxx }}` 被替换 |
| 8 | 错误重试 | 步骤 7 | 重试后继续执行 |
| 9 | E2E 完整流程 | 步骤 8 | HTTP→If→Code 全部成功 |

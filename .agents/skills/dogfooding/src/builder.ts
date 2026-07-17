import type { McpClient } from './mcp-client.js';
import type { Scenario, BuilderTrace, McpStep, FinalStatus } from './types.js';

interface BuilderConfig {
  maxBuildRetries: number;
  maxExecRetries: number;
}

export class Builder {
  constructor(
    private mcp: McpClient,
    private config: BuilderConfig,
  ) {}

  async build(scenario: Scenario): Promise<BuilderTrace> {
    const steps: McpStep[] = [];
    let totalMcpCalls = 0;
    let aiRetries = 0;
    let finalStatus: FinalStatus = 'failed';

    const log = (phase: string, tool: string, request: unknown, response: unknown, errors?: McpStep['errors']) => {
      steps.push({ phase, tool, request, response, errors: errors ?? null, timestamp: new Date().toISOString() });
      totalMcpCalls++;
    };

    try {
      console.log(`[Builder s-${scenario.id}] 开始构建场景:`, scenario.title);
      
      // Phase: 发现
      console.log(`[Builder s-${scenario.id}] Phase: 发现`);
      const conventions = await this.mcp.callTool<unknown>('get_conventions', {});
      log('discover', 'get_conventions', {}, conventions);

      const catalog = await this.mcp.callTool<Array<{ name: string; category: string; typeName?: string }>>('list_node_catalog', {});
      log('discover', 'list_node_catalog', {}, catalog);
      console.log(`[Builder] catalog 共 ${catalog?.length ?? 0} 个节点`);
      console.log(`[Builder] categoryCoverage:`, scenario.categoryCoverage);

      // 用场景的 categoryCoverage 选节点，不调 LLM
      // 注意：catalog 的字段是 name 而非 typeName
      const chosenTypes: string[] = scenario.categoryCoverage
        .map(cat => catalog?.find((n: { name: string; category: string }) => n.category === cat))
        .filter(Boolean)
        .map((n: { name: string }) => n.name);
      
      if (chosenTypes.length === 0) {
        const firstNode = catalog?.[0];
        if (firstNode?.name) chosenTypes.push(firstNode.name);
      }
      console.log(`[Builder] 选中节点:`, chosenTypes);

      // 获取每个节点的完整定义（含端口和 schema）
      interface RawNodeDetail {
        name: string;
        displayName?: string;
        category?: string;
        inputSchema?: { type: string; properties?: Record<string, unknown>; required?: string[] };
        ports?: Array<{ name: string; direction: string; type: string }>;
        isTrigger?: boolean;
      }
      const nodeDetails: RawNodeDetail[] = [];
      for (const typeName of chosenTypes) {
        const detail = await this.mcp.callTool<RawNodeDetail>('get_node_detail', { name: typeName });
        log('discover', 'get_node_detail', { name: typeName }, detail);
        if (!detail || !detail.name) {
          throw new Error(`Node detail for '${typeName}' returned empty or missing name`);
        }
        nodeDetails.push(detail);
      }

      // ── 从节点详情生成工作流节点配置 ──
      // trigger 节点排前面（入口必须在第一个）
      nodeDetails.sort((a, b) => {
        if (a.isTrigger && !b.isTrigger) return -1;
        if (!a.isTrigger && b.isTrigger) return 1;
        return 0;
      });

      const nodeList: Array<{ id: string; typeName: string; parameters: Record<string, unknown> }> = [];
      const connections: Array<{ from: string; to: string; fromPort?: string; toPort?: string }> = [];
      let entryId: string | null = null;
      let prevId: string | null = null;

      for (let i = 0; i < nodeDetails.length; i++) {
        const nd = nodeDetails[i];
        const typeName = nd.name;
        const id = typeName.replace(/([A-Z])/g, '-$1').toLowerCase().replace(/^-/, '') || `node-${i}`;

        // 生成默认参数：从 inputSchema 取默认值
        const params: Record<string, unknown> = {};
        const schema = nd.inputSchema;
        if (schema?.properties) {
          for (const [key, prop] of Object.entries(schema.properties)) {
            const p = prop as { type?: string; default?: unknown; description?: string };
            // 有默认值就用默认值
            if (p.default !== undefined) {
              params[key] = p.default;
            } else if (p.type === 'string') {
              // 字符串字段用描述或空串
              params[key] = '';
            } else if (p.type === 'number') {
              params[key] = 0;
            } else if (p.type === 'boolean') {
              params[key] = false;
            }
          }
        }

        // 特殊节点的智能默认参数
        if (typeName === 'script') {
          params['code'] ??= 'return { processed: true, input: $input.first() };';
        } else if (typeName === 'if') {
          params['condition'] ??= '$json.score >= 60';
        } else if (typeName === 'httpRequest') {
          params['url'] ??= 'https://httpbin.org/get';
          params['method'] ??= 'GET';
        } else if (typeName === 'llm') {
          params['model'] ??= 'gpt-4';
          params['prompt'] ??= '请分析输入数据';
        }

        nodeList.push({ id, typeName, parameters: params });

        // 第一个 trigger 节点设为入口
        if (nd.isTrigger && entryId === null) {
          entryId = id;
        }

        // 连接：找到前一个节点的输出端口 → 当前节点的输入端口
        if (prevId !== null) {
          const prevDetail = nodeDetails[i - 1];
          const outputPorts = prevDetail.ports?.filter(p => p.direction === 'Output') ?? [];
          const inputPorts = nd.ports?.filter(p => p.direction === 'Input') ?? [];

          // 优先选 Main 类型端口（非 AgentTool），如果没有则选第一个 Output
          const fromPort = outputPorts.find(p => p.type !== 'AgentTool')?.name
            ?? outputPorts[0]?.name;
          const toPort = inputPorts[0]?.name;

          if (!fromPort) {
            console.log(`[Builder] 警告: 节点 ${prevDetail.name} 没有 Output 端口，跳过连接`);
          } else if (!toPort) {
            console.log(`[Builder] 警告: 节点 ${nd.name} 没有 Input 端口，跳过连接`);
          } else {
            // 记录端口类型，便于调试兼容性问题
            const fromPortObj = outputPorts.find(p => p.name === fromPort);
            if (fromPortObj?.type && fromPortObj.type !== 'Main') {
              console.log(`[Builder] 注意: ${prevDetail.name}.${fromPort} 端口类型为 ${fromPortObj.type}，非 Main`);
            }
            connections.push({
              from: prevId,
              to: id,
              fromPort,
              toPort,
            });
          }
        }
        prevId = id;
      }

      // 如果没有 trigger，加一个 manualTrigger 作为入口
      if (entryId === null && nodeList.length > 0) {
        const triggerId = 'manual-trigger';
        nodeList.unshift({ id: triggerId, typeName: 'manualTrigger', parameters: {} });
        entryId = triggerId;
        // 把 trigger 连接到原来的第一个节点
        const firstNodeDetail = nodeDetails[0];
        const toPort = firstNodeDetail.ports?.find(p => p.direction === 'Input')?.name;
        if (toPort && nodeList.length > 1) {
          connections.unshift({
            from: triggerId,
            to: nodeList[1].id,
            toPort,
          });
        }
      }

      // ── 后处理：在需要数据转换的节点前插入 script 节点 ──
      const needsExtraInput = new Set(['thinkTool', 'llm', 'agent', 'subAgentTool']);
      for (let i = 0; i < nodeList.length; i++) {
        const node = nodeList[i];
        if (!needsExtraInput.has(node.typeName)) continue;

        // 找到进入该节点的连接
        const connIdx = connections.findIndex(c => c.to === node.id);
        if (connIdx < 0) continue;

        const incomingConn = connections[connIdx];
        // 检查前驱是否已经是一个 script 或 set（避免重复插入）
        const predNode = nodeList.find(n => n.id === incomingConn.from);
        if (predNode?.typeName === 'script' || predNode?.typeName === 'set') continue;

        // 插入 script 节点做准备数据
        const scriptId = `${node.id}-data-prep`;
        const scriptParams: Record<string, unknown> = {
          codeMode: 'RunOnceForAllItems',
          code: node.typeName === 'thinkTool'
            ? 'const input = $input.first();\nreturn [{ thought: JSON.stringify(input).slice(0, 1000), timestamp: new Date().toISOString() }];'
            : 'return $input.all();',
        };

        // 改连接：pred → script → node
        // ⚠️ 先 capture 旧连接的 from，因为 incomingConn 是对 connections[connIdx] 的引用
        const incomingFrom = incomingConn.from as string;
        const newFromPort = connections[connIdx].fromPort;
        connections[connIdx].from = scriptId;
        connections[connIdx].fromPort = 'Output';
        connections[connIdx].toPort = 'Input';

        // 插入 pred → script 的连接（在 connIdx 位置，使用提前 capture 的值）
        const predToScriptConn = {
          from: incomingFrom, to: scriptId,
          fromPort: newFromPort ?? undefined,
        };
        connections.splice(connIdx, 0, predToScriptConn as any);

        // 插入 script 节点到 nodeList（在 node 之前）
        nodeList.splice(i, 0, { id: scriptId, typeName: 'script', parameters: scriptParams });
        i++; // 跳过刚插入的 script，继续处理原节点

        console.log(`[Builder] 为 ${node.id}(${node.typeName}) 插入数据预处理 script 节点 ${scriptId}`);
      }

      // Phase: 构建 — assemble + validate 自纠
      console.log(`[Builder] 节点列表:`, JSON.stringify(nodeList.map(n => ({ id: n.id, typeName: n.typeName }))));
      console.log(`[Builder] 连接列表:`, JSON.stringify(connections));
      console.log(`[Builder] assemble_workflow:`, JSON.stringify({ name: scenario.title, nodesCount: nodeList.length, connectionsCount: connections.length }));
      const draftResult = await this.mcp.callTool<{ draftId: string; workflow: unknown }>('assemble_workflow', {
        name: scenario.title,
        nodes: nodeList,
        connections,
      });
      console.log(`[Builder] assemble 结果:`, JSON.stringify(draftResult).slice(0, 200));
      log('build', 'assemble_workflow', { name: scenario.title }, draftResult);
      let workflowId = draftResult.draftId;

      let buildRetries = 0;
      while (buildRetries <= this.config.maxBuildRetries) {
        console.log(`[Builder] validate_workflow (try ${buildRetries + 1})`);
        const validationResult = await this.mcp.callTool<{ valid: boolean; success?: boolean; errors?: Array<{ nodeId: string; field: string; errorType: string; canAutoFix: boolean; suggestedFix?: string }> }>('validate_workflow', { workflowId });
        log('validate', 'validate_workflow', { workflowId }, validationResult, validationResult.errors?.map(e => ({
          nodeId: e.nodeId, field: e.field, errorType: e.errorType, message: '', canAutoFix: e.canAutoFix, suggestedFix: e.suggestedFix,
        })) ?? null);

        const isValid = validationResult.valid ?? validationResult.success ?? false;
        if (isValid) {
          console.log(`[Builder] 验证通过`);
          break;
        }
        console.log(`[Builder] 验证失败:`, JSON.stringify(validationResult.errors));
        if (buildRetries >= this.config.maxBuildRetries) {
          console.log(`[Builder] 超过最大重试次数，标记为 blocker`);
          finalStatus = 'blocker';
          return { scenarioId: scenario.id, steps, totalMcpCalls, aiRetries, finalStatus };
        }

        const feedback = await this.mcp.callTool<{ rejectionReason?: string; draftStatus: string }>('get_draft_feedback', { draftId: workflowId });
        log('build', 'get_draft_feedback', { draftId: workflowId }, feedback);

        const modifyResult = await this.mcp.callTool<{ draftId: string; workflow: unknown; diff: unknown[] }>('modify_workflow', {
          workflowId,
          operations: validationResult.errors?.map(e => ({
            op: 'modify', nodeId: e.nodeId, path: `/parameters/${e.field}`, value: e.suggestedFix ?? '',
          })) ?? [],
        });
        log('build', 'modify_workflow', { workflowId, operations: validationResult.errors }, modifyResult);
        workflowId = modifyResult.draftId;
        aiRetries++;
        buildRetries++;
      }

      // Phase: 确认（可能被拒后自纠）
      console.log(`[Builder] confirm_workflow: draftId=${workflowId}`);
      let confirmResult = await this.mcp.callTool<{ id: string; isActive: boolean; rejectionReason?: string }>('confirm_workflow', { draftId: workflowId });
      console.log(`[Builder] confirm 结果:`, JSON.stringify(confirmResult));
      log('confirm', 'confirm_workflow', { draftId: workflowId }, confirmResult);

      if (!confirmResult.isActive) {
        for (let c = 0; c < 2; c++) {
          const feedback = await this.mcp.callTool<{ rejectionReason?: string; draftStatus: string }>('get_draft_feedback', { draftId: workflowId });
          log('confirm', 'get_draft_feedback', { draftId: workflowId }, feedback);

          const modifyResult = await this.mcp.callTool<{ draftId: string }>('modify_workflow', {
            workflowId, operations: [{ op: 'modify', nodeId: '*', path: '/rejectionReason', value: feedback.rejectionReason ?? '' }],
          });
          log('confirm', 'modify_workflow', { workflowId, operations: [{ op: 'modify', nodeId: '*', path: '/rejectionReason' }] }, modifyResult);
          workflowId = modifyResult.draftId;
          aiRetries++;

          confirmResult = await this.mcp.callTool<{ id: string; isActive: boolean }>('confirm_workflow', { draftId: workflowId });
          log('confirm', 'confirm_workflow', { draftId: workflowId }, confirmResult);
          if (confirmResult.isActive) break;
        }
      }

      if (!confirmResult.isActive) {
        finalStatus = 'failed';
        return { scenarioId: scenario.id, steps, totalMcpCalls, aiRetries, finalStatus };
      }

      // Phase: 修改 — 即使 happy path 也做一次 modify，确保 version > 0
      // 测试 modify_workflow 的 MCP 接口可用性，并验证修改后的 workflow 能正常执行
      // 路径格式: /nodes/{nodeId}/parameters/{field} 或 /nodes/{nodeId}/name
      const modifyTarget = nodeList.find(n => n.typeName !== 'manualTrigger' && n.typeName !== 'scheduleTrigger');
      if (modifyTarget) {
        // 优先选字符串类型的参数，避免数组/对象类型被后端类型转换后等于没改
        const stringParam = Object.entries(modifyTarget.parameters).find(([_, v]) => typeof v === 'string');
        if (stringParam) {
          const [paramName, oldVal] = stringParam;
          const newVal = `${oldVal}_tested`;
          const modifyPath = `/nodes/${modifyTarget.id}/parameters/${paramName}`;
          console.log(`[Builder] modify_workflow (happy path): ${modifyPath}`);
          const happyModifyResult = await this.mcp.callTool<{ draftId: string; workflow?: unknown }>('modify_workflow', {
            workflowId: confirmResult.id,
            operations: [{ op: 'modify', path: modifyPath, value: newVal }],
          });
          log('modify', 'modify_workflow', { workflowId: confirmResult.id, operations: [{ op: 'modify', path: modifyPath, value: newVal }] }, happyModifyResult);

          // 修改后需要重新 confirm
          const modifiedDraftId = (happyModifyResult as { draftId?: string })?.draftId ?? confirmResult.id;
          if (modifiedDraftId !== confirmResult.id) {
            console.log(`[Builder] 重新 confirm 修改后的 workflow`);
            confirmResult = await this.mcp.callTool<{ id: string; isActive: boolean }>('confirm_workflow', { draftId: modifiedDraftId });
            log('modify', 'confirm_workflow', { draftId: modifiedDraftId }, confirmResult);
          }
        }
      }

      // Phase: 结构验证 — modify 后重新 validate，捕获结构性错误
      console.log(`[Builder] 结构验证 (modify 后)`);
      const postModifyValidation = await this.mcp.callTool<{ valid: boolean; errors?: Array<{ nodeId: string; field: string; errorType: string; message: string }> }>('validate_workflow', { workflowId: confirmResult.id });
      log('validate', 'validate_workflow', { workflowId: confirmResult.id }, postModifyValidation, postModifyValidation.errors?.map(e => ({
        nodeId: e.nodeId, field: e.field, errorType: e.errorType, message: e.message ?? '', canAutoFix: false, suggestedFix: undefined,
      })) ?? null);

      if (!(postModifyValidation.valid ?? true)) {
        console.log(`[Builder] modify 后结构验证失败:`, JSON.stringify(postModifyValidation.errors));
        finalStatus = 'failed';
        return { scenarioId: scenario.id, steps, totalMcpCalls, aiRetries, finalStatus };
      }

      // Phase: 执行
      // execute_workflow 异步返回 Pending（Worker 后台处理），需轮询等待完成
      console.log(`[Builder] execute_workflow: workflowId=${confirmResult.id}`);
      const firstExecResult = await this.mcp.callTool<{ execution?: { id: string; status: string }; feedback?: unknown; success?: boolean }>('execute_workflow', { workflowId: confirmResult.id });
      console.log(`[Builder] execute 结果:`, JSON.stringify(firstExecResult));
      log('execute', 'execute_workflow', { workflowId: confirmResult.id }, firstExecResult);

      const execId = firstExecResult?.execution?.id ?? confirmResult.id;
      let execStatus = firstExecResult?.execution?.status ?? 'Pending';
      const baseUrl = this.mcp.url || 'http://localhost:8001';
      const apiKey = this.mcp.apiKey || '';

      // 轮询等待执行完成（Worker 通常在 <500ms 内处理完）
      for (let poll = 0; poll < 10 && execStatus === 'Pending'; poll++) {
        await new Promise(r => setTimeout(r, 500));
        try {
          const resp = await fetch(`${baseUrl}/api/v1/executions/${execId}`, {
            headers: apiKey ? { Authorization: `Bearer ${apiKey}` } : {},
          });
          if (resp.ok) {
            const data = await resp.json() as { status?: string };
            execStatus = data.status ?? execStatus;
            console.log(`[Builder] 轮询 #${poll + 1}: 执行状态 = ${execStatus}`);
          }
        } catch {
          // 忽略网络错误继续轮询
        }
      }

      console.log(`[Builder] 最终执行状态: ${execStatus}`);

      // 始终收集所有节点的执行记录（无论成功还是失败）
      await this.collectNodeRecords(execId, steps);

      // 执行后验证：检查节点级别结果
      if (execStatus === 'Completed') {
        const nodeIssues = this.checkNodeExecution(execId, nodeList, steps);
        if (nodeIssues.length > 0) {
          console.log(`[Builder] 节点执行问题:`, nodeIssues);
          finalStatus = 'failed';
        } else {
          finalStatus = 'completed';
        }
      } else {
        finalStatus = 'failed';
      }
    } catch (err) {
      console.log(`[Builder] catch 异常:`, err);
      finalStatus = 'blocker';
      steps.push({
        phase: 'error', tool: 'unknown', request: {}, response: String(err),
        errors: [{ errorType: 'UnhandledError', message: String(err), canAutoFix: false }],
        timestamp: new Date().toISOString(),
      });
    }

    return { scenarioId: scenario.id, steps, totalMcpCalls, aiRetries, finalStatus };
  }

  private async collectNodeRecords(execId: string, steps: McpStep[]): Promise<void> {
    const baseUrl = this.mcp.url || 'http://localhost:8001';
    const apiKey = this.mcp.apiKey || '';

    try {
      const execResp = await fetch(`${baseUrl}/api/v1/executions/${execId}`, {
        headers: apiKey ? { Authorization: `Bearer ${apiKey}` } : {},
      });
      if (!execResp.ok) return;

      interface NodeRecord {
        nodeDefinitionId: string;
        status: string;
        output?: Record<string, unknown>;
      }
      const execDetail = await execResp.json() as { nodeRecords?: NodeRecord[] };
      if (!execDetail.nodeRecords) return;

      for (const nr of execDetail.nodeRecords) {
        if (nr.status === 'Failed' && nr.output?.error) {
          const errorInfo = nr.output.error as { code?: string; message?: string };
          steps.push({
            phase: 'execute', tool: 'execution_trace',
            request: { executionId: execId },
            response: { nodeId: nr.nodeDefinitionId, error: errorInfo },
            errors: [{
              errorType: errorInfo.code ?? 'NodeFailed',
              message: `节点 ${nr.nodeDefinitionId} 执行失败: ${errorInfo.message ?? '未知错误'}`,
              canAutoFix: false,
            }],
            timestamp: new Date().toISOString(),
          });
        } else if (nr.status === 'Skipped') {
          steps.push({
            phase: 'execute', tool: 'execution_trace',
            request: { executionId: execId },
            response: { nodeId: nr.nodeDefinitionId, status: nr.status },
            errors: [{
              errorType: 'NodeSkipped',
              message: `节点 ${nr.nodeDefinitionId} 被跳过`,
              canAutoFix: false,
            }],
            timestamp: new Date().toISOString(),
          });
        }
        // Completed 节点不记录——无错误信息，进 steps 只增加噪音
      }
    } catch {
      // 忽略执行详情抓取失败
    }
  }

  /**
   * 检查节点级别执行结果：是否所有节点都执行了、是否有输出。
   * 返回问题列表，空数组表示正常。
   */
  private async checkNodeExecution(
    execId: string,
    nodeList: Array<{ id: string; typeName: string }>,
    steps: McpStep[],
  ): Promise<string[]> {
    const issues: string[] = [];
    const baseUrl = this.mcp.url || 'http://localhost:8001';
    const apiKey = this.mcp.apiKey || '';

    try {
      const resp = await fetch(`${baseUrl}/api/v1/executions/${execId}`, {
        headers: apiKey ? { Authorization: `Bearer ${apiKey}` } : {},
      });
      if (!resp.ok) return issues;

      interface NodeRecord {
        nodeDefinitionId: string;
        status: string;
        output?: Record<string, unknown>;
      }
      const detail = await resp.json() as { nodeRecords?: NodeRecord[] };
      if (!detail.nodeRecords) return issues;

      const recordMap = new Map(detail.nodeRecords.map(r => [r.nodeDefinitionId, r]));

      // 检查：工作流中的每个节点是否都有执行记录
      for (const node of nodeList) {
        const record = recordMap.get(node.id);
        if (!record) {
          issues.push(`节点 ${node.id} (${node.typeName}) 没有执行记录`);
          steps.push({
            phase: 'execute', tool: 'execution_trace',
            request: { executionId: execId },
            response: { nodeId: node.id, status: 'Missing' },
            errors: [{
              errorType: 'NodeMissing',
              message: `节点 ${node.id} (${node.typeName}) 没有执行记录`,
              canAutoFix: false,
            }],
            timestamp: new Date().toISOString(),
          });
        } else if (record.status === 'Completed') {
          // 检查输出是否为空
          const output = record.output;
          if (!output || (typeof output === 'object' && Object.keys(output).length === 0)) {
            issues.push(`节点 ${node.id} (${node.typeName}) 执行完成但输出为空`);
          }
        }
      }
    } catch {
      // 忽略检查失败
    }

    return issues;
  }
}

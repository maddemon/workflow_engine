import { createClient, type ApiClientOptions } from '../api/client.js';
import { getConfig, type ConfigOptions } from '../config.js';
import { CLIError, ErrorCode, ExitCode } from '../errors.js';
import { isJsonMode, isVerbose, log, verbose, writeJson } from '../output.js';
import type {
  CreateWorkflowDto,
  ImportResult,
  ImportWorkflowRequest,
  NodeTypeDescriptorDto,
  PagedResult,
  UpdateWorkflowDto,
  WorkflowDto,
  WorkflowExportResult,
  WorkflowSummaryDto,
} from '../types.js';
import { readFileSync } from 'node:fs';
import { BUILT_IN_NODE_TYPES } from './builtInNodeTypes.js';

export interface WorkflowListOptions {
  page?: number;
  pageSize?: number;
  projectId?: string;
  profile?: string;
  configOptions?: ConfigOptions;
}

export interface WorkflowGetOptions {
  id: string;
  version?: number;
  profile?: string;
  configOptions?: ConfigOptions;
}

export interface WorkflowVersionsOptions {
  id: string;
  profile?: string;
  configOptions?: ConfigOptions;
}

export interface WorkflowCreateOptions {
  file: string;
  name?: string;
  projectId?: string;
  dryRun?: boolean;
  profile?: string;
  configOptions?: ConfigOptions;
}

export interface WorkflowUpdateOptions {
  id: string;
  file?: string;
  name?: string;
  active?: string;
  profile?: string;
  configOptions?: ConfigOptions;
}

export interface WorkflowDeleteOptions {
  id: string;
  confirm?: boolean;
  profile?: string;
  configOptions?: ConfigOptions;
}

export interface WorkflowExportOptions {
  id: string;
  output?: string;
  profile?: string;
  configOptions?: ConfigOptions;
}

export interface WorkflowImportOptions {
  file: string;
  projectId?: string;
  dryRun?: boolean;
  profile?: string;
  configOptions?: ConfigOptions;
}

export interface WorkflowValidateOptions {
  file: string;
  profile?: string;
  configOptions?: ConfigOptions;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function createApiClient(profile?: string, configOptions?: ConfigOptions) {
  const config = getConfig(profile, configOptions);
  const options: ApiClientOptions = {
    baseURL: `${config.baseUrl}/api/v1`,
    token: config.token,
    apiKey: config.apiKey,
    verbose: isVerbose(),
  };
  return createClient(options);
}

function resolveCreatedBy(config: { userId?: string; email?: string }): string {
  if (config.userId && config.userId.length > 0) {
    return config.userId;
  }
  if (config.email && config.email.length > 0) {
    return config.email;
  }
  throw new CLIError(
    '无法确定 CreatedBy：当前 profile 没有 userId 或 email，请先登录',
    ErrorCode.ValidationError,
    ExitCode.InvocationError,
  );
}

function requireString(value: string | undefined, label: string): string {
  const trimmed = value?.trim() ?? '';
  if (trimmed.length === 0) {
    throw new CLIError(
      `缺少 ${label}`,
      ErrorCode.ValidationError,
      ExitCode.InvocationError,
    );
  }
  return trimmed;
}

function readJsonFile(filePath: string): unknown {
  let content: string;
  try {
    content = readFileSync(filePath, 'utf-8');
  } catch (err) {
    const message = err instanceof Error ? err.message : String(err);
    throw new CLIError(
      `读取文件失败：${message}`,
      ErrorCode.ConfigReadError,
      ExitCode.InvocationError,
      err,
    );
  }

  try {
    return JSON.parse(content);
  } catch (err) {
    const message = err instanceof Error ? err.message : String(err);
    throw new CLIError(
      `解析 JSON 失败：${message}`,
      ErrorCode.InvalidConfig,
      ExitCode.InvocationError,
      err,
    );
  }
}

interface NodeTypeSchema {
  typeName: string;
  ports: Array<{ name: string; direction: string; type: string }>;
  parameters: Array<{ name: string; required: boolean }>;
}

function getBuiltInNodeTypeMap(): Map<string, NodeTypeSchema> {
  const map = new Map<string, NodeTypeSchema>();
  for (const nodeType of BUILT_IN_NODE_TYPES) {
    map.set(nodeType.typeName, {
      typeName: nodeType.typeName,
      ports: nodeType.ports,
      parameters: nodeType.parameters,
    });
  }
  return map;
}

async function fetchNodeTypeSchemas(
  profile?: string,
  configOptions?: ConfigOptions,
): Promise<Map<string, NodeTypeSchema>> {
  const schemas = getBuiltInNodeTypeMap();
  try {
    const client = createApiClient(profile, configOptions);
    const response = await client.get('/node-types');
    const data: unknown = response.data;
    let nodeTypes: NodeTypeDescriptorDto[] = [];
    if (Array.isArray(data)) {
      nodeTypes = data as NodeTypeDescriptorDto[];
    } else if (isRecord(data) && Array.isArray(data.items)) {
      nodeTypes = data.items as NodeTypeDescriptorDto[];
    }
    for (const nodeType of nodeTypes) {
      schemas.set(nodeType.typeName, nodeType);
    }
  } catch (err) {
    verbose('无法从后端获取节点类型，回退到内置 schema');
  }
  return schemas;
}

function isNonEmptyValue(value: unknown): boolean {
  if (value === undefined || value === null) return false;
  if (typeof value === 'string') return value.trim().length > 0;
  if (Array.isArray(value)) return value.length > 0;
  if (isRecord(value)) return Object.keys(value).length > 0;
  return true;
}

interface ValidationResult {
  valid: boolean;
  errors: string[];
  warnings: string[];
}

function validateWorkflow(raw: unknown, schemas: Map<string, NodeTypeSchema>): ValidationResult {
  const errors: string[] = [];
  const warnings: string[] = [];

  if (!isRecord(raw)) {
    errors.push('工作流文件必须是 JSON 对象');
    return { valid: false, errors, warnings };
  }

  if (typeof raw.name !== 'string' || raw.name.trim().length === 0) {
    errors.push('工作流名称不能为空');
  }

  const nodesValid = Array.isArray(raw.nodes) && raw.nodes.length > 0;
  if (!nodesValid) {
    errors.push('nodes 必须是非空数组');
  }

  const connectionsValid = Array.isArray(raw.connections);
  if (!connectionsValid) {
    errors.push('connections 必须是数组');
  }

  if (!nodesValid) {
    return { valid: false, errors, warnings };
  }

  const nodes = raw.nodes as unknown[];
  const connections = raw.connections as unknown[];

  const nodeMap = new Map<string, Record<string, unknown>>();
  let entryCount = 0;

  for (let i = 0; i < nodes.length; i++) {
    const node = nodes[i];
    const prefix = `nodes[${i}]`;
    if (!isRecord(node)) {
      errors.push(`${prefix} 必须是对象`);
      continue;
    }
    if (typeof node.id !== 'string' || node.id.length === 0) {
      errors.push(`${prefix} 缺少有效的 id`);
      continue;
    }
    if (typeof node.typeName !== 'string' || node.typeName.length === 0) {
      errors.push(`${prefix} 缺少有效的 typeName`);
      continue;
    }
    if (node.isEntry === true) {
      entryCount++;
    }
    nodeMap.set(node.id, node);
  }

  if (entryCount === 0) {
    errors.push('至少需要一个入口节点（isEntry = true）');
  }

  for (const [id, node] of nodeMap.entries()) {
    const typeName = String(node.typeName);
    const schema = schemas.get(typeName);
    if (!schema) {
      errors.push(`节点 "${id}" 使用了未知的节点类型 "${typeName}"`);
      continue;
    }

    const nodeParameters = isRecord(node.parameters) ? node.parameters : {};
    for (const param of schema.parameters) {
      if (param.required && !isNonEmptyValue(nodeParameters[param.name])) {
        errors.push(`节点 "${id}" (${typeName}) 缺少必填参数 "${param.name}"`);
      }
    }
  }

  if (!connectionsValid) {
    return { valid: false, errors, warnings };
  }

  for (let i = 0; i < connections.length; i++) {
    const conn = connections[i];
    const prefix = `connections[${i}]`;
    if (!isRecord(conn)) {
      errors.push(`${prefix} 必须是对象`);
      continue;
    }

    const sourceId = typeof conn.sourceNodeId === 'string' ? conn.sourceNodeId : '';
    const targetId = typeof conn.targetNodeId === 'string' ? conn.targetNodeId : '';
    const sourcePort = typeof conn.sourcePortName === 'string' ? conn.sourcePortName : '';
    const targetPort = typeof conn.targetPortName === 'string' ? conn.targetPortName : '';

    if (!sourceId || !nodeMap.has(sourceId)) {
      errors.push(`${prefix} 引用了不存在的源节点 "${sourceId}"`);
      continue;
    }
    if (!targetId || !nodeMap.has(targetId)) {
      errors.push(`${prefix} 引用了不存在的目标节点 "${targetId}"`);
      continue;
    }

    const sourceNode = nodeMap.get(sourceId)!;
    const targetNode = nodeMap.get(targetId)!;
    const sourceSchema = schemas.get(String(sourceNode.typeName));
    const targetSchema = schemas.get(String(targetNode.typeName));

    if (sourceSchema) {
      const port = sourceSchema.ports.find((p) => p.name === sourcePort);
      if (!port) {
        errors.push(
          `${prefix} 源节点 "${sourceId}" (${sourceNode.typeName}) 不存在 Output 端口 "${sourcePort}"`,
        );
      } else if (port.direction !== 'Output') {
        errors.push(
          `${prefix} 源端口 "${sourcePort}" 必须是 Output 端口（当前为 ${port.direction}）`,
        );
      }
    }

    if (targetSchema) {
      const port = targetSchema.ports.find((p) => p.name === targetPort);
      if (!port) {
        errors.push(
          `${prefix} 目标节点 "${targetId}" (${targetNode.typeName}) 不存在 Input 端口 "${targetPort}"`,
        );
      } else if (port.direction !== 'Input') {
        errors.push(
          `${prefix} 目标端口 "${targetPort}" 必须是 Input 端口（当前为 ${port.direction}）`,
        );
      }
    }
  }

  return { valid: errors.length === 0, errors, warnings };
}

export async function workflowValidate(options: WorkflowValidateOptions): Promise<void> {
  const filePath = requireString(options.file, '--file');
  const raw = readJsonFile(filePath);
  const schemas = await fetchNodeTypeSchemas(options.profile, options.configOptions);
  const result = validateWorkflow(raw, schemas);

  if (isJsonMode()) {
    writeJson(result);
    if (!result.valid) {
      process.exitCode = ExitCode.InvocationError;
    }
    return;
  }

  for (const warning of result.warnings) {
    log(`[警告] ${warning}`);
  }

  if (result.valid) {
    log('工作流校验通过');
    return;
  }

  for (const error of result.errors) {
    log(`[错误] ${error}`);
  }

  throw new CLIError(
    `工作流校验失败：存在 ${result.errors.length} 处错误\n${result.errors.join('\n')}`,
    ErrorCode.ValidationError,
    ExitCode.InvocationError,
  );
}

function parseActive(value: string | undefined): boolean | undefined {
  if (value === undefined) {
    return undefined;
  }
  const normalized = value.trim().toLowerCase();
  if (normalized === 'true') return true;
  if (normalized === 'false') return false;
  throw new CLIError(
    `--active 必须是 true 或 false，收到：${value}`,
    ErrorCode.ValidationError,
    ExitCode.InvocationError,
  );
}

async function confirmDelete(resource: string, id: string): Promise<void> {
  if (isJsonMode()) {
    throw new CLIError(
      `JSON 模式下必须使用 --confirm 确认删除 ${resource}`,
      ErrorCode.ValidationError,
      ExitCode.InvocationError,
    );
  }
  const { createInterface } = await import('node:readline/promises');
  const rl = createInterface({
    input: process.stdin,
    output: process.stderr,
  });
  try {
    const answer = await rl.question(`确认删除 ${resource} ${id} 吗？(yes/no): `);
    if (answer.trim().toLowerCase() !== 'yes') {
      throw new CLIError(
        '已取消删除',
        ErrorCode.UserInterrupted,
        ExitCode.UserInterrupted,
      );
    }
  } finally {
    rl.close();
  }
}

export async function workflowList(options: WorkflowListOptions): Promise<void> {
  const client = createApiClient(options.profile, options.configOptions);
  const params: Record<string, unknown> = {};
  if (options.page !== undefined) params.page = options.page;
  if (options.pageSize !== undefined) params.pageSize = options.pageSize;
  if (options.projectId !== undefined && options.projectId.length > 0) {
    params.projectId = options.projectId;
  }

  const response = await client.get('/workflows', { params });
  const data: unknown = response.data;
  let workflows: WorkflowSummaryDto[];
  if (Array.isArray(data)) {
    workflows = data as WorkflowSummaryDto[];
  } else if (isRecord(data) && Array.isArray(data.items)) {
    workflows = data.items as WorkflowSummaryDto[];
  } else {
    workflows = [];
  }

  if (isJsonMode()) {
    writeJson(workflows);
    return;
  }

  if (workflows.length === 0) {
    log('暂无工作流。');
    return;
  }

  for (const workflow of workflows) {
    const projectId = workflow.projectId ? `, Project: ${workflow.projectId}` : '';
    log(
      `${workflow.id}: ${workflow.name} (v${workflow.version}, active=${workflow.isActive}${projectId})`,
    );
  }
}

export async function workflowGet(options: WorkflowGetOptions): Promise<void> {
  const id = requireString(options.id, '工作流 ID');
  const client = createApiClient(options.profile, options.configOptions);

  let response;
  if (options.version !== undefined) {
    response = await client.get(
      `/workflows/${encodeURIComponent(id)}/versions/${options.version}`,
    );
  } else {
    response = await client.get(`/workflows/${encodeURIComponent(id)}`);
  }

  const workflow = response.data as WorkflowDto;

  if (isJsonMode()) {
    writeJson(workflow);
    return;
  }

  log(`ID: ${workflow.id}`);
  log(`Name: ${workflow.name}`);
  log(`Version: ${workflow.version}`);
  log(`IsActive: ${workflow.isActive}`);
  if (workflow.projectId) {
    log(`ProjectId: ${workflow.projectId}`);
  }
  log(`CreatedBy: ${workflow.createdBy}`);
  log(`CreatedAt: ${workflow.createdAt}`);
  if (workflow.updatedAt) {
    log(`UpdatedAt: ${workflow.updatedAt}`);
  }
}

export async function workflowVersions(options: WorkflowVersionsOptions): Promise<void> {
  const id = requireString(options.id, '工作流 ID');
  const client = createApiClient(options.profile, options.configOptions);
  const response = await client.get(`/workflows/${encodeURIComponent(id)}/versions`);
  const data: unknown = response.data;
  let versions: WorkflowDto[];
  if (Array.isArray(data)) {
    versions = data as WorkflowDto[];
  } else if (isRecord(data) && Array.isArray(data.items)) {
    versions = data.items as WorkflowDto[];
  } else {
    versions = [];
  }

  if (isJsonMode()) {
    writeJson(versions);
    return;
  }

  if (versions.length === 0) {
    log('暂无版本记录。');
    return;
  }

  for (const version of versions) {
    log(`v${version.version}: ${version.name} (active=${version.isActive})`);
  }
}

function buildCreateWorkflowDto(
  options: WorkflowCreateOptions,
  config: { userId?: string; email?: string },
): CreateWorkflowDto {
  const raw = readJsonFile(options.file);
  if (!isRecord(raw)) {
    throw new CLIError(
      '工作流文件必须是 JSON 对象',
      ErrorCode.InvalidConfig,
      ExitCode.InvocationError,
    );
  }

  const name = options.name?.trim() || (typeof raw.name === 'string' ? raw.name : '');
  if (name.length === 0) {
    throw new CLIError(
      '缺少工作流名称：请在工作流文件中提供 name 或使用 --name',
      ErrorCode.ValidationError,
      ExitCode.InvocationError,
    );
  }

  const projectId =
    options.projectId !== undefined
      ? options.projectId.length > 0
        ? options.projectId
        : undefined
      : typeof raw.projectId === 'string'
        ? raw.projectId
        : undefined;

  const nodes = Array.isArray(raw.nodes) ? raw.nodes : [];
  const connections = Array.isArray(raw.connections) ? raw.connections : [];

  const dto: CreateWorkflowDto = {
    name,
    createdBy: resolveCreatedBy(config),
    nodes: nodes as CreateWorkflowDto['nodes'],
    connections: connections as CreateWorkflowDto['connections'],
  };

  if (projectId !== undefined) {
    dto.projectId = projectId;
  }

  if (isRecord(raw.styleSettings)) {
    dto.styleSettings = raw.styleSettings as unknown as CreateWorkflowDto['styleSettings'];
  }

  return dto;
}

export async function workflowCreate(options: WorkflowCreateOptions): Promise<void> {
  const filePath = requireString(options.file, '--file');
  const config = getConfig(options.profile, options.configOptions);
  const dto = buildCreateWorkflowDto({ ...options, file: filePath }, config);

  if (options.dryRun) {
    const raw = readJsonFile(filePath);
    const schemas = await fetchNodeTypeSchemas(options.profile, options.configOptions);
    const validation = validateWorkflow(raw, schemas);
    if (!validation.valid) {
      for (const error of validation.errors) {
        log(`[错误] ${error}`);
      }
      throw new CLIError(
        `工作流校验失败：存在 ${validation.errors.length} 处错误\n${validation.errors.join('\n')}`,
        ErrorCode.ValidationError,
        ExitCode.InvocationError,
      );
    }

    if (isJsonMode()) {
      writeJson({ dryRun: true, requestBody: dto });
      return;
    }
    log('Dry-run 模式，请求体如下：');
    log(JSON.stringify(dto, undefined, 2));
    return;
  }

  const client = createApiClient(options.profile, options.configOptions);
  const response = await client.post('/workflows', dto);
  const workflow = response.data as WorkflowDto;

  if (isJsonMode()) {
    writeJson(workflow);
    return;
  }

  log(`已创建工作流：${workflow.id}`);
  log(`Name: ${workflow.name}`);
  log(`Version: ${workflow.version}`);
}

export async function workflowUpdate(options: WorkflowUpdateOptions): Promise<void> {
  const id = requireString(options.id, '工作流 ID');

  const hasFile = options.file !== undefined && options.file.trim().length > 0;
  const hasName = options.name !== undefined && options.name.trim().length > 0;
  const hasActive = options.active !== undefined;

  if (!hasFile && !hasName && !hasActive) {
    throw new CLIError(
      '至少提供 --file、--name 或 --active 之一',
      ErrorCode.ValidationError,
      ExitCode.InvocationError,
    );
  }

  const body: UpdateWorkflowDto = {
    name: '',
    isActive: false,
    nodes: [],
    connections: [],
  };

  if (hasFile) {
    const raw = readJsonFile(options.file as string);
    if (!isRecord(raw)) {
      throw new CLIError(
        '工作流文件必须是 JSON 对象',
        ErrorCode.InvalidConfig,
        ExitCode.InvocationError,
      );
    }
    body.name = options.name?.trim() || (typeof raw.name === 'string' ? raw.name : id);
    body.isActive = parseActive(options.active) ?? (typeof raw.isActive === 'boolean' ? raw.isActive : true);
    if (isRecord(raw.styleSettings)) {
      body.styleSettings = raw.styleSettings as unknown as UpdateWorkflowDto['styleSettings'];
    }
    body.nodes = Array.isArray(raw.nodes) ? (raw.nodes as UpdateWorkflowDto['nodes']) : [];
    body.connections = Array.isArray(raw.connections)
      ? (raw.connections as UpdateWorkflowDto['connections'])
      : [];
  } else {
    body.name = options.name?.trim() || id;
    body.isActive = parseActive(options.active) ?? true;
    body.nodes = [];
    body.connections = [];
  }

  const client = createApiClient(options.profile, options.configOptions);
  const response = await client.put(`/workflows/${encodeURIComponent(id)}`, body);
  const workflow = response.data as WorkflowDto;

  if (isJsonMode()) {
    writeJson(workflow);
    return;
  }

  log(`已更新工作流：${workflow.id}`);
  log(`Name: ${workflow.name}`);
  log(`IsActive: ${workflow.isActive}`);
}

export async function workflowDelete(options: WorkflowDeleteOptions): Promise<void> {
  const id = requireString(options.id, '工作流 ID');

  if (!options.confirm) {
    await confirmDelete('工作流', id);
  }

  const client = createApiClient(options.profile, options.configOptions);
  await client.delete(`/workflows/${encodeURIComponent(id)}`);

  const output = { success: true, id };

  if (isJsonMode()) {
    writeJson(output);
    return;
  }

  log(`已删除工作流：${id}`);
}

export async function workflowExport(options: WorkflowExportOptions): Promise<void> {
  const id = requireString(options.id, '工作流 ID');
  const client = createApiClient(options.profile, options.configOptions);
  const response = await client.get(`/workflows/${encodeURIComponent(id)}/export`);
  const result = response.data as WorkflowExportResult;
  const json = JSON.stringify(result, undefined, 2);

  if (options.output !== undefined && options.output.length > 0) {
    const { writeFileSync } = await import('node:fs');
    try {
      writeFileSync(options.output, json, 'utf-8');
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err);
      throw new CLIError(
        `写入文件失败：${message}`,
        ErrorCode.ConfigWriteError,
        ExitCode.InvocationError,
        err,
      );
    }

    if (isJsonMode()) {
      writeJson({ success: true, output: options.output });
      return;
    }

    log(`已导出到：${options.output}`);
    return;
  }

  if (isJsonMode()) {
    writeJson(result);
    return;
  }

  log(json);
}

export async function workflowImport(options: WorkflowImportOptions): Promise<void> {
  const filePath = requireString(options.file, '文件路径');
  const config = getConfig(options.profile, options.configOptions);
  const raw = readJsonFile(filePath);

  const requestBody: ImportWorkflowRequest = {
    json: JSON.stringify(raw),
    importedBy: resolveCreatedBy(config),
  };

  if (options.projectId !== undefined && options.projectId.length > 0) {
    requestBody.projectId = options.projectId;
  }

  if (options.dryRun) {
    if (isJsonMode()) {
      writeJson({ dryRun: true, requestBody });
      return;
    }
    log('Dry-run 模式，请求体如下：');
    log(JSON.stringify(requestBody, undefined, 2));
    return;
  }

  const client = createApiClient(options.profile, options.configOptions);
  const response = await client.post('/workflows/import', requestBody);
  const result = response.data as ImportResult;

  if (isJsonMode()) {
    writeJson(result);
    return;
  }

  if (result.success) {
    log(`已导入工作流：${result.workflowId ?? ''} (${result.workflowName ?? ''})`);
  } else {
    log('导入失败');
  }
  if (result.errors && result.errors.length > 0) {
    for (const err of result.errors) {
      log(`  [${err.errorType}] ${err.message}`);
    }
  }
}

import { createClient, type ApiClientOptions } from '../api/client.js';
import { getConfig, type ConfigOptions } from '../config.js';
import { CLIError, ErrorCode, ExitCode } from '../errors.js';
import { isJsonMode, isVerbose, log, writeJson } from '../output.js';
import { ParameterType, type NodeTypeDescriptorDto } from '../types.js';

export interface NodeTypesListOptions {
  category?: string;
  profile?: string;
  configOptions?: ConfigOptions;
}

export interface NodeTypesGetOptions {
  typeName: string;
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

async function fetchNodeTypes(
  client: ReturnType<typeof createClient>,
  category?: string,
): Promise<NodeTypeDescriptorDto[]> {
  const response = await client.get('/node-types', {
    params: category ? { category } : undefined,
  });

  const data: unknown = response.data;
  if (Array.isArray(data)) {
    return data as NodeTypeDescriptorDto[];
  }

  if (isRecord(data) && Array.isArray(data.items)) {
    return data.items as NodeTypeDescriptorDto[];
  }

  return [];
}

function normalizeTypeName(value: string): string {
  return value.trim().toLowerCase();
}

function buildSummary(nodeType: NodeTypeDescriptorDto) {
  return {
    typeName: nodeType.typeName,
    displayName: nodeType.displayName,
    category: nodeType.category,
    parameters: nodeType.parameters.map((p) => p.name),
    ports: nodeType.ports.map((p) => p.direction),
  };
}

function isCredentialParameter(parameter: { type: ParameterType | string; credentialType?: string }): boolean {
  return parameter.type === ParameterType.Credential || Boolean(parameter.credentialType);
}

export async function nodeTypesList(options: NodeTypesListOptions): Promise<void> {
  const client = createApiClient(options.profile, options.configOptions);
  const nodeTypes = await fetchNodeTypes(client, options.category);
  const summaries = nodeTypes.map(buildSummary);

  if (isJsonMode()) {
    writeJson(summaries);
    return;
  }

  if (summaries.length === 0) {
    log('未找到节点类型。');
    return;
  }

  for (const summary of summaries) {
    log(`[${summary.category}] ${summary.typeName} (${summary.displayName})`);
    if (summary.parameters.length > 0) {
      log(`  参数: ${summary.parameters.join(', ')}`);
    }
    if (summary.ports.length > 0) {
      log(`  端口: ${summary.ports.join(', ')}`);
    }
  }
}

export async function nodeTypesGet(options: NodeTypesGetOptions): Promise<void> {
  const target = normalizeTypeName(options.typeName);
  if (target.length === 0) {
    throw new CLIError(
      '请提供节点类型名称',
      ErrorCode.ValidationError,
      ExitCode.InvocationError,
    );
  }

  const client = createApiClient(options.profile, options.configOptions);
  const nodeTypes = await fetchNodeTypes(client);
  const matched = nodeTypes.find((n) => normalizeTypeName(n.typeName) === target);

  if (!matched) {
    throw new CLIError(
      `未找到节点类型：${options.typeName}`,
      ErrorCode.NotFound,
      ExitCode.BusinessFailure,
    );
  }

  if (isJsonMode()) {
    writeJson(matched);
    return;
  }

  log(`TypeName: ${matched.typeName}`);
  log(`DisplayName: ${matched.displayName}`);
  log(`Category: ${matched.category}`);
  log(`ExecutionMode: ${matched.executionMode}`);
  log(`DefaultIsEntry: ${matched.defaultIsEntry}`);

  if (matched.parameters.length > 0) {
    log('Parameters:');
    for (const p of matched.parameters) {
      const credentialNote = isCredentialParameter(p) ? ' [需先创建凭据]' : '';
      const defaultValue = p.defaultValue !== undefined ? ` = ${JSON.stringify(p.defaultValue)}` : '';
      log(
        `  - ${p.name} (${p.type}, 必填: ${p.required}${defaultValue})${credentialNote}`,
      );
      if (p.hint) {
        log(`    Hint: ${p.hint}`);
      }
      if (p.validationRules && p.validationRules.length > 0) {
        log(`    ValidationRules: ${p.validationRules.map((r) => r.ruleType).join(', ')}`);
      }
    }
  }

  if (matched.ports.length > 0) {
    log('Ports:');
    for (const p of matched.ports) {
      log(`  - ${p.name} [${p.direction}, ${p.type}]`);
    }
  }
}

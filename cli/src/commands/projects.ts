import { createClient, type ApiClientOptions } from '../api/client.js';
import { getConfig, type ConfigOptions } from '../config.js';
import { CLIError, ErrorCode, ExitCode } from '../errors.js';
import { isJsonMode, isVerbose, log, writeJson } from '../output.js';
import type { ProjectDto } from '../types.js';

export interface ProjectListOptions {
  profile?: string;
  configOptions?: ConfigOptions;
}

export interface ProjectGetOptions {
  id: string;
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

async function fetchProjects(
  client: ReturnType<typeof createClient>,
): Promise<ProjectDto[]> {
  const response = await client.get('/projects');
  const data: unknown = response.data;

  if (Array.isArray(data)) {
    return data as ProjectDto[];
  }

  if (isRecord(data) && Array.isArray(data.items)) {
    return data.items as ProjectDto[];
  }

  return [];
}

export async function projectList(options: ProjectListOptions): Promise<void> {
  const client = createApiClient(options.profile, options.configOptions);
  const projects = await fetchProjects(client);

  if (isJsonMode()) {
    writeJson(projects);
    return;
  }

  if (projects.length === 0) {
    log('暂无项目。');
    return;
  }

  for (const project of projects) {
    const description = project.description ? ` - ${project.description}` : '';
    log(`${project.id}: ${project.name}${description} (${project.createdAt})`);
  }
}

export async function projectGet(options: ProjectGetOptions): Promise<void> {
  const id = options.id.trim();
  if (id.length === 0) {
    throw new CLIError(
      '请提供项目 ID',
      ErrorCode.ValidationError,
      ExitCode.InvocationError,
    );
  }

  const client = createApiClient(options.profile, options.configOptions);
  const response = await client.get(`/projects/${encodeURIComponent(id)}`);
  const project = response.data as ProjectDto;

  if (isJsonMode()) {
    writeJson(project);
    return;
  }

  log(`ID: ${project.id}`);
  log(`Name: ${project.name}`);
  if (project.description) {
    log(`Description: ${project.description}`);
  }
  log(`CreatedBy: ${project.createdBy}`);
  log(`CreatedAt: ${project.createdAt}`);
  if (project.updatedAt) {
    log(`UpdatedAt: ${project.updatedAt}`);
  }
}

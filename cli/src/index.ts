#!/usr/bin/env node
import { Command } from 'commander';
import pkg from '../package.json' with { type: 'json' };
import { CLIError, ErrorCode, ExitCode } from './errors.js';
import { error, isJsonMode, log, setOutputOptions, writeJson } from './output.js';
import { login, logout, me, profile } from './commands/auth.js';
import {
  apiKeyCreate,
  apiKeyList,
  apiKeyRevoke,
} from './commands/api-keys.js';
import {
  execute,
  executionCancel,
  executionGet,
  executionList,
} from './commands/executions.js';
import {
  configGet,
  configListProfiles,
  configSet,
  configUseProfile,
} from './commands/config.js';
import {
  credentialCreate,
  credentialDelete,
  credentialEnsure,
  credentialGet,
  credentialList,
  credentialUpdate,
} from './commands/credentials.js';
import { guide } from './commands/guide.js';
import { nodeTypesGet, nodeTypesList } from './commands/node-types.js';
import { projectGet, projectList } from './commands/projects.js';
import { skill } from './commands/skill.js';
import { test } from './commands/test.js';
import {
  triggerCreate,
  triggerDelete,
  triggerGet,
  triggerList,
  triggerUpdate,
} from './commands/triggers.js';
import {
  workflowCreate,
  workflowDelete,
  workflowExport,
  workflowGet,
  workflowImport,
  workflowList,
  workflowUpdate,
  workflowVersions,
} from './commands/workflows.js';

const program = new Command();

function placeholderAction(commandName: string) {
  return () => {
    if (isJsonMode()) {
      writeJson({ status: 'not-implemented', command: commandName });
      return;
    }
    log(`${commandName} 命令尚未实现。`);
  };
}

program
  .name('flowengine')
  .description('Flow Engine AI Agent CLI')
  .version(pkg.version)
  .option('--json', '输出 JSON 格式到 stdout')
  .option('--verbose', '打印详细请求/响应日志')
  .option('--profile <name>', '使用的配置 profile', 'default');

program.hook('preAction', (thisCommand) => {
  const opts = thisCommand.optsWithGlobals<{
    json?: boolean;
    verbose?: boolean;
    profile?: string;
  }>();
  setOutputOptions({
    json: opts.json ?? false,
    verbose: opts.verbose ?? false,
  });
});

program
  .command('login')
  .description('登录并保存认证信息')
  .option('--url <url>', '后端服务地址')
  .option('--email <email>', '登录邮箱')
  .option('--password <password>', '登录密码')
  .option('--api-key <key>', 'API Key')
  .option('--password-stdin', '从标准输入读取密码')
  .action(async function () {
    const command = this;
    const opts = command.optsWithGlobals<{
      url?: string;
      email?: string;
      password?: string;
      apiKey?: string;
      passwordStdin?: boolean;
      profile?: string;
    }>();
    await login({
      url: opts.url,
      email: opts.email,
      password: opts.password,
      apiKey: opts.apiKey,
      passwordStdin: opts.passwordStdin,
      profile: opts.profile,
    });
  });

program
  .command('logout')
  .description('登出当前会话')
  .action(async function () {
    const command = this;
    const opts = command.optsWithGlobals<{ profile?: string }>();
    await logout({ profile: opts.profile });
  });

program
  .command('profile')
  .description('显示当前 profile 认证信息')
  .action(async function () {
    const command = this;
    const opts = command.optsWithGlobals<{ profile?: string }>();
    await profile({ profile: opts.profile });
  });

const configCmd = program
  .command('config')
  .description('配置管理');

configCmd
  .command('get')
  .description('获取当前配置')
  .action(async function () {
    const command = this;
    const opts = command.optsWithGlobals<{ profile?: string }>();
    await configGet({ profile: opts.profile });
  });

configCmd
  .command('set <key> <value>')
  .description('设置配置项（仅支持 baseUrl、email）')
  .action(async function (key: string, value: string) {
    const command = this;
    const opts = command.optsWithGlobals<{ profile?: string }>();
    await configSet({ profile: opts.profile, key, value });
  });

configCmd
  .command('use-profile <name>')
  .description('切换默认 profile')
  .action(async (name: string) => {
    await configUseProfile({ name });
  });

configCmd
  .command('list-profiles')
  .description('列出所有已保存的 profile')
  .action(async () => {
    await configListProfiles({});
  });

const nodeTypesCmd = program.command('node-types').description('节点类型管理');

nodeTypesCmd
  .command('list')
  .description('列出节点类型')
  .option('--category <category>', '按分类过滤')
  .action(async function () {
    const command = this;
    const opts = command.optsWithGlobals<{
      category?: string;
      profile?: string;
    }>();
    await nodeTypesList({
      category: opts.category,
      profile: opts.profile,
    });
  });

nodeTypesCmd
  .command('get <typeName>')
  .description('查看节点类型详情')
  .action(async function (typeName: string) {
    const command = this;
    const opts = command.optsWithGlobals<{ profile?: string }>();
    await nodeTypesGet({ typeName, profile: opts.profile });
  });

const projectCmd = program.command('project').description('项目管理');

projectCmd
  .command('list')
  .description('列出项目')
  .action(async function () {
    const command = this;
    const opts = command.optsWithGlobals<{ profile?: string }>();
    await projectList({ profile: opts.profile });
  });

projectCmd
  .command('get <id>')
  .description('查看项目详情')
  .action(async function (id: string) {
    const command = this;
    const opts = command.optsWithGlobals<{ profile?: string }>();
    await projectGet({ id, profile: opts.profile });
  });

program
  .command('guide')
  .description('生成 DSL 编写指南')
  .option('--output <file>', '输出到文件')
  .action(async function () {
    const command = this;
    const opts = command.optsWithGlobals<{
      output?: string;
      profile?: string;
    }>();
    await guide({ output: opts.output, profile: opts.profile });
  });

program
  .command('skill')
  .description('生成 Skill 内容')
  .option('--format <format>', '输出格式：claude、cursor、mcp、json', 'claude')
  .option('--output <file>', '输出到文件')
  .action(async function () {
    const command = this;
    const opts = command.optsWithGlobals<{
      format?: string;
      output?: string;
      profile?: string;
    }>();
    await skill({
      format: opts.format as 'claude' | 'cursor' | 'mcp' | 'json',
      output: opts.output,
      profile: opts.profile,
    });
  });

program
  .command('me')
  .description('获取当前用户信息')
  .action(async function () {
    const command = this;
    const opts = command.optsWithGlobals<{ profile?: string }>();
    await me({ profile: opts.profile });
  });

const apiKeysCreateCmd = new Command('create')
  .description('创建 API Key')
  .requiredOption('--name <name>', 'API Key 名称')
  .option('--expires-at <date>', '过期时间（ISO 8601）')
  .action(async function () {
    const command = this;
    const opts = command.optsWithGlobals<{
      name?: string;
      expiresAt?: string;
      profile?: string;
    }>();
    await apiKeyCreate({
      name: opts.name!,
      expiresAt: opts.expiresAt,
      profile: opts.profile,
    });
  });

const apiKeysListCmd = new Command('list')
  .description('列出 API Key')
  .action(async function () {
    const command = this;
    const opts = command.optsWithGlobals<{ profile?: string }>();
    await apiKeyList({ profile: opts.profile });
  });

const apiKeysRevokeCmd = new Command('revoke')
  .description('吊销 API Key')
  .argument('<id>', 'API Key ID')
  .option('--confirm', '确认吊销')
  .action(async function (id: string) {
    const command = this;
    const opts = command.optsWithGlobals<{ confirm?: boolean; profile?: string }>();
    await apiKeyRevoke({ id, confirm: opts.confirm, profile: opts.profile });
  });

program
  .command('api-keys')
  .description('管理 API Key')
  .addCommand(apiKeysCreateCmd)
  .addCommand(apiKeysListCmd)
  .addCommand(apiKeysRevokeCmd);

program
  .command('execute <workflow-id>')
  .description('执行工作流')
  .option('--wait', '等待执行到终态')
  .option('--test', '执行测试（等价于 --wait + 断言）')
  .option('--timeout <seconds>', '等待超时时间（秒）', parseInt)
  .option('--idempotency-key <key>', '幂等键')
  .option('--input <json>', '输入参数 JSON')
  .option('--poll-interval <ms>', '轮询间隔（毫秒）', parseInt)
  .option('--expect <file>', '期望结果 JSON 文件')
  .action(async function (workflowId: string) {
    const command = this;
    const opts = command.optsWithGlobals<{
      wait?: boolean;
      test?: boolean;
      timeout?: number;
      idempotencyKey?: string;
      input?: string;
      pollInterval?: number;
      expect?: string;
      profile?: string;
    }>();
    await execute({
      workflowId,
      wait: opts.wait,
      test: opts.test,
      timeout: opts.timeout,
      idempotencyKey: opts.idempotencyKey,
      input: opts.input,
      pollInterval: opts.pollInterval,
      expect: opts.expect,
      profile: opts.profile,
    });
  });

const executionCmd = program.command('execution').description('执行记录管理');

executionCmd
  .command('get <id>')
  .description('查看执行详情')
  .action(async function (id: string) {
    const command = this;
    const opts = command.optsWithGlobals<{ profile?: string }>();
    await executionGet({ id, profile: opts.profile });
  });

executionCmd
  .command('list')
  .description('列出工作流执行记录')
  .requiredOption('--workflow <id>', '工作流 ID')
  .option('--page <N>', '页码', parseInt)
  .option('--page-size <N>', '每页数量', parseInt)
  .action(async function () {
    const command = this;
    const opts = command.optsWithGlobals<{
      workflow?: string;
      page?: number;
      pageSize?: number;
      profile?: string;
    }>();
    await executionList({
      workflowId: opts.workflow ?? '',
      page: opts.page,
      pageSize: opts.pageSize,
      profile: opts.profile,
    });
  });

executionCmd
  .command('cancel <id>')
  .description('取消执行')
  .action(async function (id: string) {
    const command = this;
    const opts = command.optsWithGlobals<{ profile?: string }>();
    await executionCancel({ id, profile: opts.profile });
  });

program
  .command('test')
  .description('工作流 Dry-Run 测试')
  .requiredOption('--file <file>', '工作流 JSON 文件')
  .option('--expect <file>', '期望结果 JSON 文件')
  .option('--credentials <json>', '凭据 JSON')
  .option('--timeout <seconds>', '超时时间（秒）', parseInt)
  .option('--project-id <id>', '项目 ID')
  .action(async function () {
    const command = this;
    const opts = command.optsWithGlobals<{
      file: string;
      expect?: string;
      credentials?: string;
      timeout?: number;
      projectId?: string;
      profile?: string;
    }>();
    await test({
      file: opts.file,
      expect: opts.expect,
      credentials: opts.credentials,
      timeout: opts.timeout,
      projectId: opts.projectId,
      profile: opts.profile,
    });
  });

program
  .command('dry-run')
  .description('工作流 Dry-Run 执行')
  .action(placeholderAction('dry-run'));

const credentialCmd = program.command('credential').description('凭据管理');

credentialCmd
  .command('list')
  .description('列出凭据')
  .option('--project-id <id>', '按项目过滤')
  .action(async function () {
    const command = this;
    const opts = command.optsWithGlobals<{
      projectId?: string;
      profile?: string;
    }>();
    await credentialList({
      projectId: opts.projectId,
      profile: opts.profile,
    });
  });

credentialCmd
  .command('get <id>')
  .description('查看凭据详情')
  .action(async function (id: string) {
    const command = this;
    const opts = command.optsWithGlobals<{ profile?: string }>();
    await credentialGet({ id, profile: opts.profile });
  });

credentialCmd
  .command('create')
  .description('创建凭据')
  .requiredOption('--name <name>', '凭据名称')
  .requiredOption('--type <type>', '凭据类型')
  .requiredOption('--fields <json>', '凭据字段 JSON')
  .option('--project-id <id>', '所属项目')
  .action(async function () {
    const command = this;
    const opts = command.optsWithGlobals<{
      name: string;
      type: string;
      fields: string;
      projectId?: string;
      profile?: string;
    }>();
    await credentialCreate({
      name: opts.name,
      type: opts.type,
      fields: opts.fields,
      projectId: opts.projectId,
      profile: opts.profile,
    });
  });

credentialCmd
  .command('ensure')
  .description('确保凭据存在（不存在则创建，存在则更新）')
  .requiredOption('--name <name>', '凭据名称')
  .requiredOption('--type <type>', '凭据类型')
  .requiredOption('--fields <json>', '凭据字段 JSON')
  .option('--project-id <id>', '所属项目')
  .action(async function () {
    const command = this;
    const opts = command.optsWithGlobals<{
      name: string;
      type: string;
      fields: string;
      projectId?: string;
      profile?: string;
    }>();
    await credentialEnsure({
      name: opts.name,
      type: opts.type,
      fields: opts.fields,
      projectId: opts.projectId,
      profile: opts.profile,
    });
  });

credentialCmd
  .command('update <id>')
  .description('更新凭据')
  .requiredOption('--name <name>', '凭据名称')
  .requiredOption('--fields <json>', '凭据字段 JSON')
  .action(async function (id: string) {
    const command = this;
    const opts = command.optsWithGlobals<{
      name: string;
      fields: string;
      profile?: string;
    }>();
    await credentialUpdate({
      id,
      name: opts.name,
      fields: opts.fields,
      profile: opts.profile,
    });
  });

credentialCmd
  .command('delete <id>')
  .description('删除凭据')
  .option('--confirm', '跳过确认提示')
  .action(async function (id: string) {
    const command = this;
    const opts = command.optsWithGlobals<{
      confirm?: boolean;
      profile?: string;
    }>();
    await credentialDelete({
      id,
      confirm: opts.confirm,
      profile: opts.profile,
    });
  });

const workflowCmd = program.command('workflow').description('工作流管理');

workflowCmd
  .command('list')
  .description('列出工作流')
  .option('--page <N>', '页码', parseInt)
  .option('--page-size <N>', '每页数量', parseInt)
  .option('--project-id <id>', '按项目过滤')
  .action(async function () {
    const command = this;
    const opts = command.optsWithGlobals<{
      page?: number;
      pageSize?: number;
      projectId?: string;
      profile?: string;
    }>();
    await workflowList({
      page: opts.page,
      pageSize: opts.pageSize,
      projectId: opts.projectId,
      profile: opts.profile,
    });
  });

workflowCmd
  .command('get <id>')
  .description('查看工作流详情')
  .option('--version <N>', '查看指定版本', parseInt)
  .action(async function (id: string) {
    const command = this;
    const opts = command.optsWithGlobals<{
      version?: number;
      profile?: string;
    }>();
    await workflowGet({ id, version: opts.version, profile: opts.profile });
  });

workflowCmd
  .command('versions <id>')
  .description('查看工作流版本历史')
  .action(async function (id: string) {
    const command = this;
    const opts = command.optsWithGlobals<{ profile?: string }>();
    await workflowVersions({ id, profile: opts.profile });
  });

workflowCmd
  .command('create')
  .description('从 JSON 文件创建工作流')
  .requiredOption('--file <file>', '工作流 JSON 文件')
  .option('--name <name>', '工作流名称')
  .option('--project-id <id>', '所属项目')
  .option('--dry-run', '仅打印请求体')
  .action(async function () {
    const command = this;
    const opts = command.optsWithGlobals<{
      file: string;
      name?: string;
      projectId?: string;
      dryRun?: boolean;
      profile?: string;
    }>();
    await workflowCreate({
      file: opts.file,
      name: opts.name,
      projectId: opts.projectId,
      dryRun: opts.dryRun,
      profile: opts.profile,
    });
  });

workflowCmd
  .command('update <id>')
  .description('更新工作流')
  .option('--file <file>', '工作流 JSON 文件')
  .option('--name <name>', '工作流名称')
  .option('--active <bool>', '是否激活')
  .action(async function (id: string) {
    const command = this;
    const opts = command.optsWithGlobals<{
      file?: string;
      name?: string;
      active?: string;
      profile?: string;
    }>();
    await workflowUpdate({
      id,
      file: opts.file,
      name: opts.name,
      active: opts.active,
      profile: opts.profile,
    });
  });

workflowCmd
  .command('delete <id>')
  .description('删除工作流')
  .option('--confirm', '跳过确认提示')
  .action(async function (id: string) {
    const command = this;
    const opts = command.optsWithGlobals<{
      confirm?: boolean;
      profile?: string;
    }>();
    await workflowDelete({ id, confirm: opts.confirm, profile: opts.profile });
  });

workflowCmd
  .command('export <id>')
  .description('导出工作流')
  .option('--output <file>', '输出到文件')
  .action(async function (id: string) {
    const command = this;
    const opts = command.optsWithGlobals<{
      output?: string;
      profile?: string;
    }>();
    await workflowExport({ id, output: opts.output, profile: opts.profile });
  });

workflowCmd
  .command('import <file>')
  .description('导入工作流')
  .option('--project-id <id>', '所属项目')
  .option('--dry-run', '仅打印请求体')
  .action(async function (file: string) {
    const command = this;
    const opts = command.optsWithGlobals<{
      projectId?: string;
      dryRun?: boolean;
      profile?: string;
    }>();
    await workflowImport({
      file,
      projectId: opts.projectId,
      dryRun: opts.dryRun,
      profile: opts.profile,
    });
  });

const triggerCmd = program.command('trigger').description('触发器管理');

triggerCmd
  .command('list')
  .description('列出触发器')
  .option('--workflow <id>', '按工作流过滤')
  .option('--project-id <id>', '按项目过滤')
  .action(async function () {
    const command = this;
    const opts = command.optsWithGlobals<{
      workflow?: string;
      projectId?: string;
      profile?: string;
    }>();
    await triggerList({
      workflow: opts.workflow,
      projectId: opts.projectId,
      profile: opts.profile,
    });
  });

triggerCmd
  .command('get <id>')
  .description('查看触发器详情')
  .action(async function (id: string) {
    const command = this;
    const opts = command.optsWithGlobals<{ profile?: string }>();
    await triggerGet({ id, profile: opts.profile });
  });

triggerCmd
  .command('create')
  .description('创建触发器')
  .requiredOption('--workflow <id>', '工作流 ID')
  .requiredOption('--type <type>', '触发器类型（Schedule/Webhook/Poll）')
  .option('--name <name>', '触发器名称')
  .option('--active', '是否激活', true)
  .option('--settings <json>', '触发器设置 JSON')
  .action(async function () {
    const command = this;
    const opts = command.optsWithGlobals<{
      workflow: string;
      type: string;
      name?: string;
      active?: boolean;
      settings?: string;
      profile?: string;
    }>();
    await triggerCreate({
      workflow: opts.workflow,
      type: opts.type,
      name: opts.name,
      active: opts.active,
      settings: opts.settings,
      profile: opts.profile,
    });
  });

triggerCmd
  .command('update <id>')
  .description('更新触发器')
  .option('--name <name>', '触发器名称')
  .option('--active <bool>', '是否激活')
  .option('--settings <json>', '触发器设置 JSON')
  .action(async function (id: string) {
    const command = this;
    const opts = command.optsWithGlobals<{
      name?: string;
      active?: string;
      settings?: string;
      profile?: string;
    }>();
    await triggerUpdate({
      id,
      name: opts.name,
      active: opts.active,
      settings: opts.settings,
      profile: opts.profile,
    });
  });

triggerCmd
  .command('delete <id>')
  .description('删除触发器')
  .option('--confirm', '跳过确认提示')
  .action(async function (id: string) {
    const command = this;
    const opts = command.optsWithGlobals<{
      confirm?: boolean;
      profile?: string;
    }>();
    await triggerDelete({ id, confirm: opts.confirm, profile: opts.profile });
  });

async function main(): Promise<void> {
  try {
    await program.parseAsync();
  } catch (err) {
    if (err instanceof CLIError) {
      if (isJsonMode()) {
        writeJson({
          success: false,
          error: err.message,
          code: err.code,
        });
      } else {
        error(err.message);
      }
      process.exit(err.exitCode);
    }
    const message = err instanceof Error ? err.message : String(err);
    if (isJsonMode()) {
      writeJson({
        success: false,
        error: message,
        code: ErrorCode.UnexpectedError,
      });
    } else {
      error(`意外错误：${message}`);
    }
    process.exit(ExitCode.InvocationError);
  }
}

process.on('SIGINT', () => {
  process.exit(ExitCode.UserInterrupted);
});

await main();

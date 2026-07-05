#!/usr/bin/env node
import { Command } from 'commander';
import pkg from '../package.json' with { type: 'json' };
import { CLIError, ExitCode } from './errors.js';
import { error, isJsonMode, log, setOutputOptions, writeJson } from './output.js';

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
  const opts = thisCommand.opts<{
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
  .description('登录并保存 JWT Token')
  .action(placeholderAction('login'));

program
  .command('logout')
  .description('登出当前会话')
  .action(placeholderAction('logout'));

program
  .command('me')
  .description('获取当前用户信息')
  .action(placeholderAction('me'));

program
  .command('api-keys')
  .description('管理 API Key')
  .action(placeholderAction('api-keys'));

program
  .command('projects')
  .description('项目管理')
  .action(placeholderAction('projects'));

program
  .command('workflows')
  .description('工作流管理')
  .action(placeholderAction('workflows'));

program
  .command('dry-run')
  .description('工作流 Dry-Run 执行')
  .action(placeholderAction('dry-run'));

async function main(): Promise<void> {
  try {
    await program.parseAsync();
  } catch (err) {
    if (err instanceof CLIError) {
      error(err.message);
      process.exit(err.exitCode);
    }
    const message = err instanceof Error ? err.message : String(err);
    error(`意外错误：${message}`);
    process.exit(ExitCode.InvocationError);
  }
}

process.on('SIGINT', () => {
  process.exit(ExitCode.UserInterrupted);
});

await main();

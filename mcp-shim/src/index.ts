#!/usr/bin/env node
/**
 * Flow Engine MCP Shim — stdio→HTTP proxy entry point.
 *
 * Reads FLOWENGINE_URL and FLOWENGINE_API_KEY from environment,
 * then reads MCP JSON-RPC messages from stdin line-by-line,
 * forwards each to ${FLOWENGINE_URL}/mcp, and writes responses to stdout.
 */

import { createHttpClient } from './http-client.js';
import { createProxy } from './proxy.js';
import { createInterface } from 'node:readline';

async function main(): Promise<void> {
  const baseURL = process.env.FLOWENGINE_URL;
  const apiKey = process.env.FLOWENGINE_API_KEY;

  if (!baseURL) {
    process.stderr.write('Error: FLOWENGINE_URL environment variable is required\n');
    process.exit(1);
  }

  if (!apiKey) {
    process.stderr.write('Error: FLOWENGINE_API_KEY environment variable is required\n');
    process.exit(1);
  }

  const httpClient = createHttpClient({ baseURL, apiKey });
  const proxy = createProxy({
    httpClient,
    streams: {
      stdin: process.stdin,
      stdout: process.stdout,
      stderr: process.stderr,
    },
  });

  const rl = createInterface({ input: process.stdin });

  for await (const line of rl) {
    await proxy.handleMessage(line);
  }
}

main().catch((err) => {
  process.stderr.write(`Fatal: ${err instanceof Error ? err.message : String(err)}\n`);
  process.exit(1);
});

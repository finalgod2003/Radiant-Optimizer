import { spawnSync } from 'node:child_process';
import { existsSync, readFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import { dirname, resolve } from 'node:path';
import { parseEnv } from 'node:util';

const command = process.argv[2];
if (!['build', 'deploy'].includes(command)) {
  console.error('Usage: node scripts/cloudflare.mjs <build|deploy>');
  process.exit(1);
}

const require = createRequire(import.meta.url);
const env = {
  ...process.env,
  ...(existsSync('.env.production')
    ? parseEnv(readFileSync('.env.production', 'utf8'))
    : {}),
  NODE_ENV: 'production',
  NITRO_PRESET: 'cloudflare_module',
};

function run(script, args = []) {
  const result = spawnSync(process.execPath, [script, ...args], {
    env,
    stdio: 'inherit',
  });
  if (result.error) {
    console.error(result.error.message);
    process.exit(1);
  }
  if (result.status !== 0) process.exit(result.status ?? 1);
}

run('scripts/db-setup.mjs');
run(resolve(dirname(require.resolve('vite/package.json')), 'bin/vite.js'), [
  'build',
]);

if (command === 'deploy') {
  run(
    resolve(
      dirname(require.resolve('wrangler/package.json')),
      'bin/wrangler.js'
    ),
    ['deploy']
  );
}

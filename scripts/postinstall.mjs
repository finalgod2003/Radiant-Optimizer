import { spawnSync } from 'node:child_process';
import { existsSync, mkdirSync } from 'node:fs';

mkdirSync('data', { recursive: true });
if (!existsSync('src/config/db/schema.ts')) {
  const result = spawnSync(process.execPath, ['scripts/db-setup.mjs'], {
    stdio: 'inherit',
  });
  if (result.status !== 0) process.exit(result.status ?? 1);
}

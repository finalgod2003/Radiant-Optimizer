import { spawnSync } from 'node:child_process';
import { existsSync } from 'node:fs';
import { homedir } from 'node:os';
import { join } from 'node:path';

export function dotnet(args) {
  const local = join(homedir(), '.codex', 'tools', 'dotnet10', 'dotnet.exe');
  const executable =
    process.env.DOTNET_HOST_PATH ||
    (process.platform === 'win32' && existsSync(local) ? local : 'dotnet');
  const result = spawnSync(executable, args, {
    stdio: 'inherit',
    shell: false,
    env: {
      ...process.env,
      DOTNET_CLI_TELEMETRY_OPTOUT: '1',
      DOTNET_NOLOGO: '1',
    },
  });
  if (result.error) throw result.error;
  if (result.status !== 0)
    throw new Error(
      `dotnet failed (${result.status}). Install the .NET 10 SDK or set DOTNET_HOST_PATH.`
    );
}

import { spawn } from 'node:child_process';
import { existsSync } from 'node:fs';
import { mkdtemp, readFile, rm } from 'node:fs/promises';
import { homedir, tmpdir } from 'node:os';
import { join, resolve, sep } from 'node:path';

import { createDesktopActivationFixture } from './desktop-activation.fixture';

// Run through scripts/with-env.ts so the runner uses the same development DB as Vite.
// pnpm exec tsx scripts/with-env.ts tsx tests/desktop-activation.run.ts http://localhost:3010
const serverUrl = process.argv[2] || 'http://localhost:3010';
const localSdk = join(homedir(), '.codex/tools/dotnet10/dotnet.exe');
const executable =
  process.env.DOTNET_HOST_PATH || (existsSync(localSdk) ? localSdk : 'dotnet');
const runDirectory = await mkdtemp(
  join(tmpdir(), 'optimizer-http-integration-')
);
let fixture:
  | Awaited<ReturnType<typeof createDesktopActivationFixture>>
  | undefined;

try {
  // Test the same pinned public keys embedded by the desktop build, rather than
  // making the test client trust whichever key the HTTP endpoint returns.
  const trustedKeys = JSON.parse(
    await readFile(
      resolve('desktop/Radiant.App/trusted-keys.local.json'),
      'utf8'
    )
  );
  if (!Array.isArray(trustedKeys) || trustedKeys.length === 0)
    throw new Error(
      'Build an activation-enabled client before running HTTP integration.'
    );
  fixture = await createDesktopActivationFixture(serverUrl);
  console.log(
    'Local test accounts and a marked purchase fixture are ready. No real payment was made.'
  );
  const exitCode = await new Promise<number>((resolveExit, reject) => {
    const child = spawn(
      executable,
      [
        'run',
        '--project',
        'desktop/Radiant.Integration.Tests/Radiant.Integration.Tests.csproj',
        '-c',
        'Release',
      ],
      {
        shell: false,
        stdio: 'inherit',
        env: {
          ...process.env,
          DOTNET_CLI_TELEMETRY_OPTOUT: '1',
          DOTNET_NOLOGO: '1',
          OPTIMIZER_LICENSE_PRIVATE_KEY: undefined,
          OPTIMIZER_TEST_TRUSTED_KEYS: JSON.stringify(trustedKeys),
          OPTIMIZER_TEST_SERVER: serverUrl,
          OPTIMIZER_TEST_EMAIL: fixture!.buyer.email,
          OPTIMIZER_TEST_PASSWORD: fixture!.buyer.password,
          OPTIMIZER_TEST_OTHER_EMAIL: fixture!.other.email,
          OPTIMIZER_TEST_OTHER_PASSWORD: fixture!.other.password,
          OPTIMIZER_TEST_LICENSE: fixture!.licenseId,
          OPTIMIZER_TEST_DIRECTORY: runDirectory,
        },
      }
    );
    child.once('error', reject);
    child.once('exit', (code) => resolveExit(code ?? 1));
  });
  if (exitCode !== 0)
    throw new Error(`Native/server HTTP integration failed (${exitCode})`);
} finally {
  let installationIds: string[] = [];
  try {
    const result = JSON.parse(
      await readFile(join(runDirectory, 'result.json'), 'utf8')
    ) as { installationIds?: string[] };
    installationIds =
      result.installationIds?.filter((id) => /^[\w-]{16,128}$/.test(id)) || [];
  } catch {
    /* No clients were created if compilation or startup failed. */
  }
  await fixture?.cleanup(installationIds);
  if (
    !resolve(runDirectory).startsWith(
      resolve(tmpdir()) + sep + 'optimizer-http-integration-'
    )
  ) {
    throw new Error('Unexpected integration cleanup directory');
  }
  await rm(runDirectory, {
    recursive: true,
    force: true,
    maxRetries: 8,
    retryDelay: 100,
  });
  console.log(
    'Local integration fixture rows and protected client files were removed.'
  );
}

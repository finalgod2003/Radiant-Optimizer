import { spawnSync } from 'node:child_process';
import { createHash, createPrivateKey, createPublicKey } from 'node:crypto';
import { createReadStream, mkdirSync, statSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';

import { dotnet } from './desktop-runtime.mjs';
import { preparePresentMon } from './prepare-presentmon.mjs';

if (process.platform !== 'win32')
  throw new Error('This packaging script currently targets Windows x64.');
const version = '0.3.0';
const freePreview = process.argv.includes('--free-preview');
const serverUrl = new URL(
  process.env.OPTIMIZER_DESKTOP_SERVER_URL ||
    process.env.VITE_APP_URL ||
    'http://localhost:3000'
);
if (
  serverUrl.protocol !== 'https:' &&
  !(
    serverUrl.protocol === 'http:' &&
    ['localhost', '127.0.0.1', '[::1]'].includes(serverUrl.hostname)
  )
)
  throw new Error('Desktop release servers must use HTTPS.');
const settings = {
  appName: process.env.VITE_APP_NAME || 'Optimizer',
  serverUrl: serverUrl.origin,
  channel: freePreview
    ? 'unsigned free preview · activation not configured'
    : serverUrl.protocol === 'http:'
      ? 'local development preview'
      : 'unsigned preview',
};
const output = resolve('artifacts/desktop/win-x64');
const settingsPath = resolve('desktop/Radiant.App/desktop-settings.local.json');
let trustedKeys = freePreview
  ? []
  : JSON.parse(process.env.OPTIMIZER_LICENSE_PUBLIC_KEYS || '[]');
if (!Array.isArray(trustedKeys))
  throw new Error('OPTIMIZER_LICENSE_PUBLIC_KEYS must be a JSON array.');
if (
  !freePreview &&
  !trustedKeys.length &&
  process.env.OPTIMIZER_LICENSE_PRIVATE_KEY
) {
  const normalized = process.env.OPTIMIZER_LICENSE_PRIVATE_KEY.replace(
    /-----[^-]+-----/g,
    ''
  ).replace(/\s/g, '');
  const privateKey = createPrivateKey({
    key: Buffer.from(normalized, 'base64'),
    format: 'der',
    type: 'pkcs8',
  });
  trustedKeys = [
    {
      keyId: process.env.OPTIMIZER_LICENSE_KEY_ID,
      spki: createPublicKey(privateKey)
        .export({ format: 'der', type: 'spki' })
        .toString('base64url'),
    },
  ];
}
const seenKeys = new Set();
for (const key of trustedKeys) {
  if (
    !/^[A-Za-z0-9_.-]{1,80}$/.test(key.keyId || '') ||
    seenKeys.has(key.keyId)
  )
    throw new Error('Invalid or duplicate trusted key ID.');
  const publicKey = createPublicKey({
    key: Buffer.from(key.spki, 'base64url'),
    format: 'der',
    type: 'spki',
  });
  if (
    publicKey.asymmetricKeyType !== 'rsa' ||
    publicKey.asymmetricKeyDetails.modulusLength < 2048
  )
    throw new Error(
      'Trusted license keys must use RSA with at least 2048 bits.'
    );
  seenKeys.add(key.keyId);
}
if (!trustedKeys.length && !freePreview)
  throw new Error(
    'Configure trusted public license keys, or use --free-preview to explicitly build without activation.'
  );
// Only public SPKI values are embedded in the executable; no secret leaves the build process.
writeFileSync(
  resolve('desktop/Radiant.App/trusted-keys.local.json'),
  JSON.stringify(trustedKeys, null, 2) + '\n'
);
writeFileSync(settingsPath, JSON.stringify(settings, null, 2) + '\n');
mkdirSync(output, { recursive: true });
await preparePresentMon();
dotnet([
  'publish',
  'desktop/Radiant.App/Radiant.App.csproj',
  '-c',
  'Release',
  '-r',
  'win-x64',
  '--self-contained',
  'true',
  '-p:PublishSingleFile=true',
  '-p:IncludeNativeLibrariesForSelfExtract=true',
  '-p:DebugType=None',
  '-o',
  output,
  '--nologo',
]);
writeFileSync(
  resolve(output, 'READ-ME.txt'),
  `${settings.appName} ${version} / Windows x64 preview\n\nExtract this archive, then open Optimizer.exe.\nConfigured website: ${settings.serverUrl}\n\nThis local preview is not code-signed. Interface animation adjustments are opt-in per action. Game preferences, power plans and game configuration writes retain their session opt-in requirements. See WHAT-IS-NEW.txt for the current scope.\nBackups and activation data: %LOCALAPPDATA%\\OptimizerPreview\nTo stop using the app, restore any changes you want to undo, close it, and remove the extracted folder. Keep the backup directory until recovery is no longer needed.\n\nGame profiles: supported CS2, Fortnite and VALORANT config formats only. Preview is free; apply requires Premium. Game must be closed. Recovery remains free.\nMonitoring: CPU/RAM, supported Windows GPU counters and NVIDIA NVML sensors. CPU temperature is not collected. PresentMon 2.6.0 reads ETW presentation events for selected processes; permissions and rendering compatibility affect availability. FPS capture/overlay/export requires Premium. Overlay supports windowed/borderless mode, not guaranteed exclusive fullscreen.\nPresentMon is bundled under tools/PresentMon with its MIT license. No service or driver is installed; no game process is injected.\nNVIDIA driver tuning, bandwidth and GPU stress tests remain outside this preview.\n`
);
mkdirSync(resolve('public/downloads'), { recursive: true });
writeFileSync(
  resolve(output, 'WHAT-IS-NEW.txt'),
  `${settings.appName} ${version}

New: Reduce interface animations; instant minimize/restore transitions.
These use documented Windows interfaces. Each change is optional, backs up the original first, and reads back the Windows result.
Recovery: Restore all Windows changes in one click, newest first. Conflicting external changes require individual review; failed operations keep their backups.
Original backups are stored separately from execution status and are retained after restoration. Old 0.2 records remain readable.
Backups: %LOCALAPPDATA%\\OptimizerPreview\\recovery
These backups cover recorded settings, not a Windows system image. Game-file backups remain separately listed in Recovery.
Existing game recording preferences and power-plan changes retain their session opt-in and entitlement requirements.
No fixed FPS improvement is promised. Native acceptance tested both animation preferences and an installed power-plan switch followed by restoration on the development PC; other Windows configurations may differ.

${freePreview ? 'This free local build has no activation keys. Premium activation is unavailable; free system tools and existing recovery remain usable.' : 'Premium activation uses the licensing service configured in desktop-settings.local.json.'}
`
);
const filename = `optimizer-${version}-win-x64-preview.zip`;
const archive = resolve('public/downloads', filename);
// Package an explicit manifest so diagnostic files from a prior local run never
// become part of a release archive, even when the publish folder is reused.
const releaseFiles = [
  'Optimizer.exe',
  'desktop-settings.local.json',
  'READ-ME.txt',
  'WHAT-IS-NEW.txt',
  'tools/PresentMon/PresentMon.exe',
  'tools/PresentMon/LICENSE.txt',
];
for (const file of releaseFiles) {
  if (!statSync(resolve(output, file)).isFile())
    throw new Error(`Required release file is missing: ${file}`);
}
const packed = spawnSync(
  'tar.exe',
  ['-a', '-cf', archive, '-C', output, ...releaseFiles],
  {
    shell: false,
    stdio: 'inherit',
  }
);
if (packed.error) throw packed.error;
if (packed.status !== 0)
  throw new Error('Could not create the Windows archive.');
const hash = createHash('sha256');
for await (const chunk of createReadStream(archive)) hash.update(chunk);
const release = {
  version,
  downloadUrl: `/downloads/${filename}`,
  sha256: hash.digest('hex'),
  sizeBytes: statSync(archive).size,
  activationAvailable: trustedKeys.length > 0,
};
writeFileSync(
  resolve('public/downloads/optimizer-release.json'),
  JSON.stringify(release, null, 2) + '\n'
);
console.log(
  `Built ${filename} (${Math.round(release.sizeBytes / 1048576)} MB). SHA-256: ${release.sha256}`
);

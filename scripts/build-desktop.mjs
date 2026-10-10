import { spawnSync } from 'node:child_process';
import { createHash, createPrivateKey, createPublicKey } from 'node:crypto';
import { createReadStream, mkdirSync, statSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';

import { dotnet } from './desktop-runtime.mjs';
import { preparePresentMon } from './prepare-presentmon.mjs';

if (process.platform !== 'win32')
  throw new Error('This packaging script currently targets Windows x64.');
const version = '0.5.0';
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
  `${settings.appName} ${version} / Windows x64 preview\n\nExtract this archive, then open Optimizer.exe.\nConfigured website: ${settings.serverUrl}\n\nThis local preview is not code-signed. Game Boost reviews each Windows change before applying. Game-file writes additionally require session opt-in. Every recorded setting/file is backed up first. See WHAT-IS-NEW.txt for the current scope.\nBackups and activation data: %LOCALAPPDATA%\\OptimizerPreview\nTo stop using the app, restore any changes you want to undo, close it, and remove the extracted folder. Keep the backup directory until recovery is no longer needed.\n\nGame profiles: supported CS2, Fortnite and VALORANT config formats only. Performance presets and their FPS cap choices are Free; Competitive and Balanced application requires Premium. Game must be closed. Recovery remains free.\nMonitoring: CPU/RAM, supported Windows GPU counters and NVIDIA NVML sensors. CPU temperature is not collected. PresentMon 2.6.0 reads ETW presentation events for selected processes; permissions and rendering compatibility affect availability. FPS capture/overlay/export requires Premium. Overlay supports windowed/borderless mode, not guaranteed exclusive fullscreen.\nPresentMon is bundled under tools/PresentMon with its MIT license. No service or driver is installed; no game process is injected.\nNVIDIA driver tuning, bandwidth and GPU stress tests remain outside this preview.\n`
);
mkdirSync(resolve('public/downloads'), { recursive: true });
writeFileSync(
  resolve(output, 'WHAT-IS-NEW.txt'),
  `${settings.appName} ${version}

New in 0.5.0: Premium custom game profiles and CPU control.
Game profiles: choose individual supported settings, leave the others unchanged, save a named setup and load it for a fresh preview. CS2: VSync/MSAA. Fortnite: frame cap, VSync and existing graphics quality fields. VALORANT: always-on frame cap and existing material/detail quality. Custom-profile application and saving require Premium. Exact original game files are backed up before applying. Existing Free Performance presets remain Free.
CPU control: inspect supported running CS2/Fortnite/VALORANT processes, review Normal/Above normal priority and optional logical-processor selection. Changes affect that exact process instance only. Originals are persisted before every write. Restore in Recovery while the game runs; process exit naturally ends those process settings. PID reuse never targets a replacement process. Core selection requires a single processor group; protected games may refuse access. Restricting CPUs can reduce performance; compare your own runs. No game injection, security changes or automatic elevation.
Recovery now includes Windows, game files and CPU settings in one free action, even without Premium. Exact originals and external conflicts remain available.

Included from 0.4.1: Premium device activation is available in builds with embedded public verification keys. Open Your license, select Activate this PC, then sign into the configured website and approve the device with your existing Premium license. Online refresh and offline verification use signed seven-day leases; backups and recovery stay Free.

Included from 0.4: Game Boost and game Performance presets.
Gaming essentials: enable Windows Game Mode and turn off built-in game/background recording in one backed-up operation. Game clips become unavailable until restored.
Plugged-in performance: request Best Performance through the supported Windows 11 AC power-mode API. Higher heat/fan noise/power use are possible. Battery policy is unchanged; thermal and firmware constraints can override the effective result.
Free Performance game presets: CS2 VSync/MSAA off; Fortnite lower existing shadows/effects/postprocess/foliage and low textures; VALORANT low material/detail. Default preserves the current FPS cap. Fortnite/VALORANT also offer optional 60/90/120/144/165/240/360 FPS ceilings. A cap is not a promised frame rate.
Only validated configuration schemas are writable. Preview exact differences, close the game, then back up and apply. Resolution, bindings and sensitivity stay unchanged. Existing Competitive/Balanced profiles retain Premium requirements.
Recovery: Restore all changes covers both Windows settings and original game files, newest first within each journal. External edits and running games block the affected restoration; other independent records can still recover. Forced game-file restoration also backs up the external file first.
Backups: %LOCALAPPDATA%\\OptimizerPreview\\recovery, \\game-profiles and \\cpu-recovery. Saved presets live in \\saved-game-profiles.
Keep the backup folders. Old journals remain readable. These are recorded-setting/file backups, not a full Windows system image.
No fixed FPS improvement is promised. Native Windows apply/reload/restore acceptance and exact-byte game fixture tests are documented in the repository; actual gains depend on the game and PC.

${freePreview ? 'This free local build has no activation keys. Premium activation is unavailable; Free Game Boost, Performance game presets and recovery remain usable.' : 'Premium activation uses the licensing service configured in desktop-settings.local.json.'}
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

import { createHash } from 'node:crypto';
import { copyFile, mkdir, readFile, rename, writeFile } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

export const presentMon = Object.freeze({
  version: '2.6.0',
  url: 'https://github.com/GameTechDev/PresentMon/releases/download/v2.6.0/PresentMon-2.6.0-x64.exe',
  sizeBytes: 980320,
  sha256: 'b2a706bc6ad475749e3b7e3409263aa1e6906d45bdcf993f6dbc0f660188f1af',
});
const projectRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..');

export async function preparePresentMon() {
  const target = resolve(
    projectRoot,
    'artifacts/tool-downloads/PresentMon/PresentMon.exe'
  );
  let bytes;
  try {
    bytes = await readFile(target);
  } catch (error) {
    if (error.code !== 'ENOENT') throw error;
  }
  const valid = (value) =>
    value?.length === presentMon.sizeBytes &&
    createHash('sha256').update(value).digest('hex') === presentMon.sha256;
  if (!valid(bytes)) {
    const response = await fetch(presentMon.url, {
      signal: AbortSignal.timeout(30000),
    });
    if (!response.ok)
      throw new Error(`PresentMon download failed (${response.status}).`);
    const chunks = [];
    let total = 0;
    for await (const chunk of response.body) {
      total += chunk.length;
      if (total > presentMon.sizeBytes)
        throw new Error('PresentMon download exceeded its pinned size.');
      chunks.push(chunk);
    }
    bytes = Buffer.concat(chunks);
    if (!valid(bytes))
      throw new Error('PresentMon release integrity check failed.');
    await mkdir(dirname(target), { recursive: true });
    await writeFile(target + '.tmp', bytes);
    await rename(target + '.tmp', target);
  }
  await copyFile(
    resolve(projectRoot, 'desktop/third-party/PresentMon.LICENSE.txt'),
    resolve(dirname(target), 'LICENSE.txt')
  );
  console.log(
    `PresentMon ${presentMon.version} verified (${presentMon.sha256}).`
  );
  return target;
}

if (
  process.argv[1] &&
  resolve(process.argv[1]) === fileURLToPath(import.meta.url)
)
  await preparePresentMon();

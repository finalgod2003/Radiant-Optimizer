// Materialize the ignored local files required by Nitro and cf:deploy in CI.
// This profile contains public settings only; secrets stay in Cloudflare.
import { existsSync, readFileSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';

const profile = readFileSync(
  new URL('../wrangler.production.json', import.meta.url),
  'utf8'
);
const { vars } = JSON.parse(profile);
const productionEnv = Object.entries(vars)
  .map(([key, value]) => {
    // Both Vite and the app's env loader must preserve embedded JSON quotes.
    const text = String(value);
    const quote = ["'", '"'].find((candidate) => !text.includes(candidate));
    if (!quote || /[\r\n]/u.test(text)) {
      throw new Error(
        `Cloudflare CI: ${key} must be a single-line dotenv value`
      );
    }
    return `${key}=${quote}${text}${quote}`;
  })
  .join('\n');

for (const [file, contents] of [
  ['wrangler.jsonc', profile],
  ['.env.production', `${productionEnv}\n`],
]) {
  if (existsSync(resolve(file))) {
    console.log(`Cloudflare CI: preserving existing ${file}`);
    continue;
  }
  writeFileSync(resolve(file), contents, { flag: 'wx' });
  console.log(`Cloudflare CI: created ${file}`);
}

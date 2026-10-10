import { generateKeyPairSync } from 'node:crypto';
import { appendFileSync, existsSync, readFileSync } from 'node:fs';
import { resolve } from 'node:path';

// Local bootstrap only. Never rotates an existing key or emits private material.
const target = resolve('.env.development');
if (!existsSync(target))
  throw new Error('Create .env.development from .env.example first.');
const text = readFileSync(target, 'utf8');
const privateMatch = text.match(/^OPTIMIZER_LICENSE_PRIVATE_KEY=(.*)$/m);
const idMatch = text.match(/^OPTIMIZER_LICENSE_KEY_ID=(.*)$/m);
if (privateMatch?.[1]?.trim() && idMatch?.[1]?.trim()) {
  console.log('Existing local signing key preserved.');
} else {
  if (privateMatch?.[1]?.trim() || idMatch?.[1]?.trim())
    throw new Error(
      'Incomplete existing signing configuration; repair it without rotating the private key.'
    );
  const { privateKey } = generateKeyPairSync('rsa', { modulusLength: 3072 });
  const encoded = privateKey
    .export({ format: 'der', type: 'pkcs8' })
    .toString('base64url');
  // Appending overrides the blank template entries for dotenv and node --env-file.
  appendFileSync(
    target,
    `\n# Persistent local-only license signing key\nOPTIMIZER_LICENSE_KEY_ID=local-preview-1\nOPTIMIZER_LICENSE_PRIVATE_KEY=${encoded}\n`
  );
  console.log(
    'Created a persistent signing key in .env.development. Private material was not printed.'
  );
}

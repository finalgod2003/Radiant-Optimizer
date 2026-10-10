import { createPrivateKey, generateKeyPairSync } from 'node:crypto';
import { appendFileSync, existsSync, readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { parseEnv } from 'node:util';

// Local bootstrap only. Never rotates an existing key or emits private material.
const target = resolve('.env.development');
if (!existsSync(target))
  throw new Error('Create .env.development from .env.example first.');
const text = readFileSync(target, 'utf8');
// Match dotenv's quoted blanks and final assignment, so running setup again
// preserves a key that was appended after the blank template values.
const env = parseEnv(text);
const savedPrivate = env.OPTIMIZER_LICENSE_PRIVATE_KEY?.trim();
const savedId = env.OPTIMIZER_LICENSE_KEY_ID?.trim();
if (savedPrivate && savedId) {
  if (!/^[A-Za-z0-9_.-]{1,80}$/.test(savedId))
    throw new Error(
      'Invalid existing key ID; the signing key was not changed.'
    );
  const key = createPrivateKey({
    key: Buffer.from(
      savedPrivate.replace(/-----[^-]+-----/g, '').replace(/\s/g, ''),
      'base64'
    ),
    format: 'der',
    type: 'pkcs8',
  });
  if (
    key.asymmetricKeyType !== 'rsa' ||
    key.asymmetricKeyDetails.modulusLength < 2048
  )
    throw new Error(
      'Existing license key must be RSA with at least 2048 bits.'
    );
  console.log('Existing local signing key preserved.');
} else {
  if (savedPrivate || savedId)
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

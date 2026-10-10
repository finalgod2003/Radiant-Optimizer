import { LICENSE_LEASE_DAYS, licenseSigningConfig } from '@/config/optimizer';
import type {
  LicenseLeasePayload,
  SignedLicenseLease,
} from '@/lib/optimizer-contracts';

export function base64url(bytes: ArrayBuffer | Uint8Array): string {
  return btoa(String.fromCharCode(...new Uint8Array(bytes)))
    .replace(/\+/g, '-')
    .replace(/\//g, '_')
    .replace(/=+$/, '');
}

function decodeBase64(value: string): Uint8Array<ArrayBuffer> {
  const normalized = value
    .replace(/-----[^-]+-----/g, '')
    .replace(/\s/g, '')
    .replace(/-/g, '+')
    .replace(/_/g, '/');
  return Uint8Array.from(atob(normalized), (c) => c.charCodeAt(0));
}

export function randomToken(): string {
  return base64url(crypto.getRandomValues(new Uint8Array(32)));
}

export async function tokenHash(token: string): Promise<string> {
  return base64url(
    await crypto.subtle.digest('SHA-256', new TextEncoder().encode(token))
  );
}

type SigningConfig = { privateKey: string; keyId: string };
let cached:
  | { source: string; keyId: string; key: CryptoKey; spki: string }
  | undefined;

async function signingKey(config: SigningConfig) {
  if (!config.privateKey || !/^[A-Za-z0-9_.-]{1,80}$/.test(config.keyId))
    throw new Error('License signing is not configured');
  if (cached?.source === config.privateKey && cached.keyId === config.keyId)
    return cached;
  const algorithm = { name: 'RSASSA-PKCS1-v1_5', hash: 'SHA-256' };
  const key = await crypto.subtle.importKey(
    'pkcs8',
    decodeBase64(config.privateKey),
    algorithm,
    true,
    ['sign']
  );
  if ((key.algorithm as RsaHashedKeyAlgorithm).modulusLength < 2048)
    throw new Error('License RSA key must be at least 2048 bits');
  const jwk = await crypto.subtle.exportKey('jwk', key);
  const pub = await crypto.subtle.importKey(
    'jwk',
    { kty: 'RSA', n: jwk.n, e: jwk.e, ext: true },
    algorithm,
    true,
    ['verify']
  );
  cached = {
    source: config.privateKey,
    keyId: config.keyId,
    key,
    spki: base64url(await crypto.subtle.exportKey('spki', pub)),
  };
  return cached;
}

export async function publicSigningKeys(config = licenseSigningConfig()) {
  const key = await signingKey(config);
  return { keys: [{ keyId: key.keyId, spki: key.spki }] };
}

export async function signLease(
  claims: Omit<LicenseLeasePayload, 'issuedAt' | 'expiresAt'>,
  now: Date,
  config = licenseSigningConfig()
): Promise<SignedLicenseLease> {
  const key = await signingKey(config);
  const payload: LicenseLeasePayload = {
    ...claims,
    issuedAt: now.toISOString(),
    expiresAt: new Date(
      now.getTime() + LICENSE_LEASE_DAYS * 86400000
    ).toISOString(),
  };
  const bytes = new TextEncoder().encode(JSON.stringify(payload));
  const signature = await crypto.subtle.sign(
    'RSASSA-PKCS1-v1_5',
    key.key,
    bytes
  );
  return {
    payload: base64url(bytes),
    signature: base64url(signature),
    keyId: key.keyId,
  };
}

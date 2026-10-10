import { envConfigs } from '@/config';
import type {
  OptimizerCatalog,
  ProductRelease,
} from '@/lib/optimizer-contracts';

export const OPTIMIZER_PRODUCT_ID = 'optimizer_premium_lifetime';
export const OPTIMIZER_PRICE_IN_CENTS = 1990;
export const OPTIMIZER_CURRENCY = 'usd';
export const DEVICE_GRANT_SECONDS = 300;
export const DEVICE_POLL_SECONDS = 5;
export const LICENSE_LEASE_DAYS = 7;

function serverValue(name: string): string {
  const binding = (globalThis as { __CF_ENV__?: Record<string, unknown> })
    .__CF_ENV__?.[name];
  return typeof binding === 'string'
    ? binding
    : typeof process !== 'undefined'
      ? process.env[name] || ''
      : '';
}

/** Server-only configuration: no private signing material belongs in VITE_* vars. */
export function licenseSigningConfig() {
  return {
    privateKey: serverValue('OPTIMIZER_LICENSE_PRIVATE_KEY'),
    keyId: serverValue('OPTIMIZER_LICENSE_KEY_ID'),
  };
}

export type OptimizerPaymentProvider = 'stripe' | 'waffo';

export function optimizerPaymentProvider(
  config: Record<string, string>,
  requested?: string
): OptimizerPaymentProvider | null {
  const defaultProvider = config.default_payment_provider;
  const selected =
    requested ||
    (defaultProvider === 'stripe' || defaultProvider === 'waffo'
      ? defaultProvider
      : undefined);
  const candidates = selected ? [selected] : ['stripe', 'waffo'];
  for (const provider of candidates) {
    if (provider === 'waffo') {
      let httpsOrigin = false;
      try {
        httpsOrigin = new URL(config.app_url).protocol === 'https:';
      } catch {
        // Waffo's payment channel requires an absolute HTTPS return URL.
      }
      if (
        config.waffo_enabled === 'true' &&
        /^MER_[A-Za-z0-9]+$/.test(config.waffo_merchant_id || '') &&
        /^STO_[A-Za-z0-9]+$/.test(config.waffo_store_id || '') &&
        /^PROD_[A-Za-z0-9]+$/.test(config.waffo_product_id || '') &&
        config.waffo_private_key &&
        config.waffo_webhook_public_key &&
        ['test', 'prod'].includes(config.waffo_environment) &&
        httpsOrigin
      )
        return 'waffo';
    }
    if (
      provider === 'stripe' &&
      config.stripe_enabled === 'true' &&
      /^sk_(test|live)_/.test(
        config.stripe_secret_key || config.stripe_api_key || ''
      ) &&
      (config.stripe_signing_secret || config.stripe_webhook_secret)
    )
      return 'stripe';
  }
  return null;
}

export function optimizerCheckoutEnabled(
  config: Record<string, string>,
  requested?: string
) {
  const signing = licenseSigningConfig();
  return Boolean(
    optimizerPaymentProvider(config, requested) &&
    signing.privateKey &&
    signing.keyId
  );
}

export function validateOptimizerRelease(
  value: unknown
): ProductRelease | null {
  if (!value || typeof value !== 'object') return null;
  const r = value as Partial<ProductRelease>;
  if (
    typeof r.version !== 'string' ||
    !/^\d+\.\d+\.\d+(?:[-+][\w.-]+)?$/.test(r.version) ||
    typeof r.downloadUrl !== 'string' ||
    typeof r.sha256 !== 'string' ||
    !/^[a-f\d]{64}$/i.test(r.sha256) ||
    !Number.isSafeInteger(r.sizeBytes) ||
    (r.sizeBytes ?? 0) <= 0
  )
    return null;
  let url: URL;
  try {
    url = new URL(r.downloadUrl, envConfigs.app_url);
  } catch {
    return null;
  }
  const isLocalPath =
    r.downloadUrl.startsWith('/downloads/') && !r.downloadUrl.includes('..');
  if (!isLocalPath && url.protocol !== 'https:') return null;
  return {
    version: r.version,
    downloadUrl: r.downloadUrl,
    sha256: r.sha256.toLowerCase(),
    sizeBytes: r.sizeBytes!,
    ...(typeof r.activationAvailable === 'boolean'
      ? { activationAvailable: r.activationAvailable }
      : {}),
  };
}

let releaseCache:
  | { manifest: string; modified: number; release: ProductRelease }
  | undefined;

/** File is generated only after a package exists; Worker deployments can inject the same JSON. */
export async function readOptimizerRelease(): Promise<ProductRelease | null> {
  try {
    const deployedManifest = serverValue('OPTIMIZER_RELEASE_JSON');
    if (deployedManifest) {
      return validateOptimizerRelease(JSON.parse(deployedManifest));
    }
    const { readFile, stat } = await import('node:fs/promises');
    const { resolve, sep } = await import('node:path');
    const root = resolve('public');
    const manifest = await readFile(
      resolve(root, 'downloads/optimizer-release.json'),
      'utf8'
    );
    const release = validateOptimizerRelease(JSON.parse(manifest));
    if (!release) return null;
    if (release.downloadUrl.startsWith('/downloads/')) {
      const target = resolve(root, '.' + release.downloadUrl);
      if (!target.startsWith(root + sep)) return null;
      const fileStat = await stat(target);
      if (!fileStat.isFile() || fileStat.size !== release.sizeBytes)
        return null;
      if (
        releaseCache?.manifest === manifest &&
        releaseCache.modified === fileStat.mtimeMs
      )
        return releaseCache.release;
      const bytes = await readFile(target);
      const hash = Array.from(
        new Uint8Array(await crypto.subtle.digest('SHA-256', bytes)),
        (b) => b.toString(16).padStart(2, '0')
      ).join('');
      if (hash !== release.sha256) return null;
      releaseCache = { manifest, modified: fileStat.mtimeMs, release };
    }
    return release;
  } catch {
    return null;
  }
}

export async function getOptimizerCatalog(
  config: Record<string, string>
): Promise<OptimizerCatalog> {
  return {
    productId: OPTIMIZER_PRODUCT_ID,
    priceInCents: OPTIMIZER_PRICE_IN_CENTS,
    currency: OPTIMIZER_CURRENCY,
    checkoutEnabled: optimizerCheckoutEnabled(config),
    release: await readOptimizerRelease(),
  };
}

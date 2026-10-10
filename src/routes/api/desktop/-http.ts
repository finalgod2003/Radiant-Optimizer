import { getRequestIP } from '@tanstack/react-start/server';
import { z } from 'zod';

import { getAuth } from '@/core/auth';
import { envConfigs } from '@/config';
import { LicenseError } from '@/modules/licenses/service';
import { respData, respErr } from '@/lib/resp';

import { createLicenseRateLimiter, licenseRequesterIdentity } from './-limits';

const requestLimiter = createLicenseRateLimiter();

export function requireSameOrigin(request: Request) {
  const origin = request.headers.get('origin');
  const allowed = [
    new URL(request.url).origin,
    new URL(envConfigs.app_url).origin,
  ];
  if (
    !origin ||
    !allowed.includes(origin) ||
    request.headers.get('sec-fetch-site') === 'cross-site'
  ) {
    throw new LicenseError('Cross-origin request rejected', 403);
  }
}

export async function requireUser(request: Request) {
  const session = await getAuth().api.getSession({ headers: request.headers });
  if (!session?.user) throw new LicenseError('Unauthorized', 401);
  return session.user;
}

export async function readJson<T>(
  request: Request,
  schema: z.ZodType<T>
): Promise<T> {
  if (!request.headers.get('content-type')?.startsWith('application/json'))
    throw new LicenseError('JSON content type required', 415);
  const reader = request.body?.getReader();
  if (!reader) throw new LicenseError('Missing JSON body');
  const chunks: Uint8Array[] = [];
  let byteLength = 0;
  try {
    while (true) {
      const { done, value } = await reader.read();
      if (done) break;
      byteLength += value.byteLength;
      if (byteLength > 8192) {
        await reader.cancel();
        throw new LicenseError('Request is too large', 413);
      }
      chunks.push(value);
    }
  } finally {
    reader.releaseLock();
  }
  const bytes = new Uint8Array(byteLength);
  let offset = 0;
  for (const chunk of chunks) {
    bytes.set(chunk, offset);
    offset += chunk.byteLength;
  }
  let text: string;
  try {
    text = new TextDecoder('utf-8', { fatal: true }).decode(bytes);
  } catch {
    throw new LicenseError('Invalid JSON encoding');
  }
  let body: unknown;
  try {
    body = JSON.parse(text);
  } catch {
    throw new LicenseError('Invalid JSON body');
  }
  const parsed = schema.safeParse(body);
  if (!parsed.success) throw new LicenseError('Invalid request fields');
  return parsed.data;
}

export async function licenseResponse(
  handler: () => Promise<unknown>
): Promise<Response> {
  try {
    return respData(await handler(), {
      headers: { 'cache-control': 'no-store' },
    });
  } catch (error) {
    if (error instanceof LicenseError)
      return respErr(error.message, {
        status: error.status,
        headers: {
          'cache-control': 'no-store',
          ...(error.status === 429 ? { 'retry-after': '5' } : {}),
        },
      });
    console.error(
      'License operation failed:',
      error instanceof Error ? error.message : 'Unknown error'
    );
    return respErr('License service unavailable', {
      status: 503,
      headers: { 'cache-control': 'no-store' },
    });
  }
}

export function limitLicenseRequest(
  request: Request,
  intervalMs: number,
  key: string
) {
  let directAddress: string | undefined;
  try {
    directAddress = getRequestIP({ xForwardedFor: false });
  } catch {
    /* Not available in an isolated route test. */
  }
  const worker =
    typeof navigator !== 'undefined' &&
    navigator.userAgent === 'Cloudflare-Workers';
  const identity = licenseRequesterIdentity(request, directAddress, worker);
  if (!requestLimiter.accept(`${key}|${identity}`, intervalMs))
    throw new LicenseError('Too many requests. Please retry shortly.', 429);
}

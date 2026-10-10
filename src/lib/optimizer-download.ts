import type { ProductRelease } from './optimizer-contracts';
import { respErr } from './resp';

type ReleaseObject = {
  size: number;
  body?: ReadableStream<Uint8Array>;
};

export type ReleaseBucket = {
  get(key: string): Promise<ReleaseObject | null>;
  head(key: string): Promise<ReleaseObject | null>;
};

/** Only the published, hash-addressed release can be read from this private bucket. */
export async function serveOptimizerDownload(
  request: Request,
  release: ProductRelease | null,
  bucket: ReleaseBucket | undefined
) {
  const unavailable = () =>
    respErr('Download unavailable', {
      status: 503,
      headers: { 'Cache-Control': 'no-store' },
    });
  if (!release || !bucket) return unavailable();
  if (new URL(request.url).searchParams.get('sha256') !== release.sha256)
    return respErr('Release not found', {
      status: 404,
      headers: { 'Cache-Control': 'no-store' },
    });
  const filename = `optimizer-${release.version}-win-x64-preview.zip`;
  const key = `${release.version}/${release.sha256}/${filename}`;
  const object =
    request.method === 'HEAD' ? await bucket.head(key) : await bucket.get(key);
  if (!object || object.size !== release.sizeBytes) {
    await object?.body?.cancel();
    return unavailable();
  }
  const headers = new Headers({
    'Content-Type': 'application/zip',
    'Content-Disposition': `attachment; filename="${filename}"`,
    'Content-Length': String(release.sizeBytes),
    'Cache-Control': 'public, max-age=3600',
    'X-Content-Type-Options': 'nosniff',
    ETag: `"sha256-${release.sha256}"`,
  });
  if (request.method === 'HEAD') return new Response(null, { headers });
  if (!object.body) return unavailable();
  // Stream the file rather than buffering a Windows installer in Worker memory.
  return new Response(object.body, { headers });
}

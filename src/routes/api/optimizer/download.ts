import { createFileRoute } from '@tanstack/react-router';

import { readOptimizerRelease } from '@/config/optimizer';
import {
  serveOptimizerDownload,
  type ReleaseBucket,
} from '@/lib/optimizer-download';
import { respErr } from '@/lib/resp';

async function download({ request }: { request: Request }) {
  try {
    const binding = (
      globalThis as { __CF_ENV__?: { OPTIMIZER_RELEASES?: ReleaseBucket } }
    ).__CF_ENV__?.OPTIMIZER_RELEASES;
    return await serveOptimizerDownload(
      request,
      await readOptimizerRelease(),
      binding
    );
  } catch {
    return respErr('Download unavailable', {
      status: 503,
      headers: { 'Cache-Control': 'no-store' },
    });
  }
}

export const Route = createFileRoute('/api/optimizer/download')({
  server: { handlers: { GET: download, HEAD: download } },
});

import { createFileRoute } from '@tanstack/react-router';
import { z } from 'zod';

import { licenses } from '@/modules/licenses/service';

import {
  licenseResponse,
  limitLicenseRequest,
  readJson,
  requireSameOrigin,
  requireUser,
} from './-http';

async function GET({ request }: { request: Request }) {
  return licenseResponse(async () => {
    const user = await requireUser(request);
    limitLicenseRequest(request, 1000, `preview:${user.id}`);
    return licenses.preview(
      new URL(request.url).searchParams.get('userCode') || ''
    );
  });
}

async function POST({ request }: { request: Request }) {
  return licenseResponse(async () => {
    requireSameOrigin(request);
    const user = await requireUser(request);
    limitLicenseRequest(request, 1000, `approve:${user.id}`);
    const input = await readJson(
      request,
      z.object({
        userCode: z.string().min(1).max(32),
        licenseId: z.string().min(1).max(128),
      })
    );
    await licenses.approve(user.id, input.userCode, input.licenseId);
    return { ok: true };
  });
}

export const Route = createFileRoute('/api/desktop/approve')({
  server: { handlers: { GET, POST } },
});

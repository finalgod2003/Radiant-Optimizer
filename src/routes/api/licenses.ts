import { createFileRoute } from '@tanstack/react-router';
import { z } from 'zod';

import { licenses } from '@/modules/licenses/service';

import {
  licenseResponse,
  limitLicenseRequest,
  readJson,
  requireSameOrigin,
  requireUser,
} from './desktop/-http';

async function GET({ request }: { request: Request }) {
  return licenseResponse(async () => {
    const user = await requireUser(request);
    await licenses.reconcileUserOrders(user.id);
    return licenses.list(user.id);
  });
}

async function POST({ request }: { request: Request }) {
  return licenseResponse(async () => {
    requireSameOrigin(request);
    const user = await requireUser(request);
    limitLicenseRequest(request, 1000, `unbind:${user.id}`);
    const input = await readJson(
      request,
      z.object({
        action: z.literal('unbind'),
        licenseId: z.string().min(1).max(128),
      })
    );
    await licenses.unbind(user.id, input.licenseId);
    return { ok: true };
  });
}

export const Route = createFileRoute('/api/licenses')({
  server: { handlers: { GET, POST } },
});

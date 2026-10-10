import { createFileRoute } from '@tanstack/react-router';
import { z } from 'zod';

import { LicenseError, licenses } from '@/modules/licenses/service';
import { hasPermission } from '@/modules/rbac/service';

import {
  licenseResponse,
  readJson,
  requireSameOrigin,
  requireUser,
} from '../desktop/-http';

async function admin(request: Request) {
  const user = await requireUser(request);
  if (!(await hasPermission(user.id, 'admin.*')))
    throw new LicenseError('Forbidden', 403);
  return user;
}

async function GET({ request }: { request: Request }) {
  return licenseResponse(async () => {
    await admin(request);
    const params = new URL(request.url).searchParams;
    const page = Math.max(
      1,
      Math.min(1000000, Number(params.get('page')) || 1)
    );
    const pageSize = Math.max(
      1,
      Math.min(100, Number(params.get('pageSize')) || 10)
    );
    return licenses.listAdmin(Math.floor(page), Math.floor(pageSize));
  });
}

async function POST({ request }: { request: Request }) {
  return licenseResponse(async () => {
    requireSameOrigin(request);
    const user = await admin(request);
    const input = await readJson(
      request,
      z.object({
        action: z.literal('revoke'),
        licenseId: z.string().min(1).max(128),
      })
    );
    await licenses.revoke(input.licenseId, 'revoked', user.id);
    return { ok: true };
  });
}

export const Route = createFileRoute('/api/admin/licenses')({
  server: { handlers: { GET, POST } },
});

import { createFileRoute } from '@tanstack/react-router';
import { z } from 'zod';

import { envConfigs } from '@/config';
import { licenses } from '@/modules/licenses/service';

import { licenseResponse, limitLicenseRequest, readJson } from './-http';

async function POST({ request }: { request: Request }) {
  return licenseResponse(async () => {
    limitLicenseRequest(request, 2000, 'start');
    const input = await readJson(
      request,
      z.object({
        deviceName: z.string().min(1).max(80),
        installationId: z.string().min(16).max(128),
      })
    );
    return licenses.start(input, envConfigs.app_url);
  });
}

export const Route = createFileRoute('/api/desktop/start')({
  server: { handlers: { POST } },
});

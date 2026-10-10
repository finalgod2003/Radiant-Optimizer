import { createFileRoute } from '@tanstack/react-router';
import { z } from 'zod';

import { licenses } from '@/modules/licenses/service';

import { licenseResponse, readJson } from './-http';

async function POST({ request }: { request: Request }) {
  return licenseResponse(async () => {
    const input = await readJson(
      request,
      z.object({ deviceCode: z.string().length(43) })
    );
    return licenses.poll(input.deviceCode);
  });
}

export const Route = createFileRoute('/api/desktop/poll')({
  server: { handlers: { POST } },
});

import { createFileRoute } from '@tanstack/react-router';

import { licenses } from '@/modules/licenses/service';

import { licenseResponse } from './-http';

async function GET() {
  return licenseResponse(() => licenses.keys());
}

export const Route = createFileRoute('/api/desktop/keys')({
  server: { handlers: { GET } },
});

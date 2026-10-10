import { createFileRoute } from '@tanstack/react-router';

import { LicenseError, licenses } from '@/modules/licenses/service';

import { licenseResponse, limitLicenseRequest } from './-http';

async function POST({ request }: { request: Request }) {
  return licenseResponse(async () => {
    limitLicenseRequest(request, 1000, 'refresh');
    const authorization = request.headers.get('authorization');
    if (!authorization?.startsWith('Bearer '))
      throw new LicenseError('Device bearer token required', 401);
    return licenses.refresh(authorization.slice(7));
  });
}

export const Route = createFileRoute('/api/desktop/refresh')({
  server: { handlers: { POST } },
});

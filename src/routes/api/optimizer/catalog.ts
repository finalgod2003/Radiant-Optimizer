import { createFileRoute } from '@tanstack/react-router';

import { getOptimizerCatalog } from '@/config/optimizer';
import { getAllConfigs } from '@/modules/config/service';
import { licenses } from '@/modules/licenses/service';

import { licenseResponse } from '../desktop/-http';

async function GET() {
  return licenseResponse(async () => {
    const catalog = await getOptimizerCatalog(await getAllConfigs());
    if (catalog.checkoutEnabled) {
      try {
        await licenses.keys();
      } catch {
        catalog.checkoutEnabled = false;
      }
    }
    return catalog;
  });
}

export const Route = createFileRoute('/api/optimizer/catalog')({
  server: { handlers: { GET } },
});

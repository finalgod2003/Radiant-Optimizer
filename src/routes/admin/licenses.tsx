import { createFileRoute } from '@tanstack/react-router';

import { m } from '@/paraglide/messages.js';
import { getLocale } from '@/paraglide/runtime.js';
import { OptimizerAdminLicenses } from '@/blocks/optimizer/admin-licenses';

export const Route = createFileRoute('/admin/licenses')({
  loader: () => ({
    title: m['optimizer.admin.title']({}, { locale: getLocale() }),
  }),
  head: ({ loaderData }) => ({
    meta: [
      { title: loaderData?.title },
      { name: 'robots', content: 'noindex, nofollow' },
    ],
  }),
  component: OptimizerAdminLicenses,
});

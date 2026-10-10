import { createFileRoute } from '@tanstack/react-router';

import { m } from '@/paraglide/messages.js';
import { getLocale } from '@/paraglide/runtime.js';
import { OptimizerLicenses } from '@/blocks/optimizer/licenses';

export const Route = createFileRoute('/settings/licenses')({
  loader: () => ({
    title: m['optimizer.licenses.title']({}, { locale: getLocale() }),
  }),
  head: ({ loaderData }) => ({
    meta: [
      { title: loaderData?.title },
      { name: 'robots', content: 'noindex, nofollow' },
    ],
  }),
  component: OptimizerLicenses,
});

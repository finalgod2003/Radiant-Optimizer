import { createFileRoute } from '@tanstack/react-router';

import { envConfigs } from '@/config';
import { m } from '@/paraglide/messages.js';
import { getLocale } from '@/paraglide/runtime.js';
import { OptimizerActivateDevice } from '@/blocks/optimizer/activate-device';
import { optimizerMetadata } from '@/blocks/optimizer/metadata';

export const Route = createFileRoute('/activate-device')({
  validateSearch: (search: Record<string, unknown>) => ({
    userCode: typeof search.userCode === 'string' ? search.userCode : undefined,
    code: typeof search.code === 'string' ? search.code : undefined,
  }),
  loader: () => {
    const locale = getLocale();
    return {
      locale,
      title: m['optimizer.meta.activate_title'](
        { name: envConfigs.app_name },
        { locale }
      ),
      description: m['optimizer.meta.activate_description']({}, { locale }),
    };
  },
  head: ({ loaderData }) => {
    const metadata = optimizerMetadata(loaderData, '/activate-device');
    return {
      ...metadata,
      meta: [
        ...metadata.meta,
        { name: 'robots', content: 'noindex, nofollow' },
      ],
    };
  },
  component: OptimizerActivateDevice,
});

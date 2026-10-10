import { createFileRoute } from '@tanstack/react-router';

import { envConfigs } from '@/config';
import { m } from '@/paraglide/messages.js';
import { getLocale } from '@/paraglide/runtime.js';
import { optimizerMetadata } from '@/blocks/optimizer/metadata';
import { OptimizerPricing } from '@/blocks/optimizer/pricing';

export const Route = createFileRoute('/pricing')({
  loader: () => {
    const locale = getLocale();
    return {
      locale,
      title: m['optimizer.meta.pricing_title'](
        { name: envConfigs.app_name },
        { locale }
      ),
      description: m['optimizer.meta.pricing_description']({}, { locale }),
    };
  },
  head: ({ loaderData }) => optimizerMetadata(loaderData, '/pricing'),
  component: OptimizerPricing,
});

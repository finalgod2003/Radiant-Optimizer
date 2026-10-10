import { createFileRoute } from '@tanstack/react-router';

import { envConfigs } from '@/config';
import { m } from '@/paraglide/messages.js';
import { getLocale } from '@/paraglide/runtime.js';
import { OptimizerFeatures } from '@/blocks/optimizer/features';
import { optimizerMetadata } from '@/blocks/optimizer/metadata';

export const Route = createFileRoute('/features')({
  loader: () => {
    const locale = getLocale();
    return {
      locale,
      title: m['optimizer.meta.features_title'](
        { name: envConfigs.app_name },
        { locale }
      ),
      description: m['optimizer.meta.features_description']({}, { locale }),
    };
  },
  head: ({ loaderData }) => optimizerMetadata(loaderData, '/features'),
  component: OptimizerFeatures,
});

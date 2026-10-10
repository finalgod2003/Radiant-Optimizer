import { createFileRoute } from '@tanstack/react-router';

import { envConfigs } from '@/config';
import { m } from '@/paraglide/messages.js';
import { getLocale } from '@/paraglide/runtime.js';
import { OptimizerTools } from '@/blocks/optimizer/libraries';
import { optimizerMetadata } from '@/blocks/optimizer/metadata';

export const Route = createFileRoute('/tools')({
  loader: () => {
    const locale = getLocale();
    return {
      locale,
      title: m['optimizer.meta.tools_title'](
        { name: envConfigs.app_name },
        { locale }
      ),
      description: m['optimizer.meta.tools_description']({}, { locale }),
    };
  },
  head: ({ loaderData }) => optimizerMetadata(loaderData, '/tools'),
  component: OptimizerTools,
});

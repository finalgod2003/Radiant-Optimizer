import { createFileRoute } from '@tanstack/react-router';

import { envConfigs } from '@/config';
import { m } from '@/paraglide/messages.js';
import { getLocale } from '@/paraglide/runtime.js';
import { OptimizerGames } from '@/blocks/optimizer/libraries';
import { optimizerMetadata } from '@/blocks/optimizer/metadata';

export const Route = createFileRoute('/games/')({
  loader: () => {
    const locale = getLocale();
    return {
      locale,
      title: m['optimizer.meta.games_title'](
        { name: envConfigs.app_name },
        { locale }
      ),
      description: m['optimizer.meta.games_description']({}, { locale }),
    };
  },
  head: ({ loaderData }) => optimizerMetadata(loaderData, '/games'),
  component: OptimizerGames,
});

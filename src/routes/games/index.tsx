import { createFileRoute } from '@tanstack/react-router';

import { envConfigs } from '@/config';
import { m } from '@/paraglide/messages.js';
import { getLocale, localizeUrl } from '@/paraglide/runtime.js';
import { RadiantGames } from '@/blocks/radiant/games';

export const Route = createFileRoute('/games/')({
  loader: () => ({
    title: `${m['radiant.product.navgames']({}, { locale: getLocale() })} | ${envConfigs.app_name}`,
    description: m['radiant.games.description']({}, { locale: getLocale() }),
    url: localizeUrl(`${envConfigs.app_url}/games`, { locale: getLocale() })
      .href,
  }),
  head: ({ loaderData }) => ({
    meta: [
      { title: loaderData?.title },
      { name: 'description', content: loaderData?.description },
    ],
    links: [{ rel: 'canonical', href: loaderData?.url }],
  }),
  component: RadiantGames,
});

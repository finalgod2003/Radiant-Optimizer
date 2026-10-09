import { createFileRoute, notFound } from '@tanstack/react-router';
import { getGame } from '@/data/games';

import { envConfigs } from '@/config';
import { getLocale, locales, localizeUrl } from '@/paraglide/runtime.js';
import { gameDescriptions } from '@/blocks/radiant/content';
import { GameDetail } from '@/blocks/radiant/game-detail';

export const Route = createFileRoute('/games/$gameId')({
  loader: ({ params }) => {
    const game = getGame(params.gameId);
    if (!game) throw notFound();
    return {
      game,
      locale: getLocale(),
      description: gameDescriptions()[game.id],
    };
  },
  head: ({ loaderData }) => {
    if (!loaderData) return {};
    const { game, locale, description } = loaderData;
    const urlFor = (loc: (typeof locales)[number]) =>
      localizeUrl(`${envConfigs.app_url}/games/${game.id}`, { locale: loc })
        .href;
    return {
      meta: [
        { title: `${game.title} | ${envConfigs.app_name}` },
        { name: 'description', content: description },
        {
          property: 'og:title',
          content: `${game.title} | ${envConfigs.app_name}`,
        },
        { property: 'og:description', content: description },
        { property: 'og:type', content: 'website' },
        { property: 'og:url', content: urlFor(locale) },
        ...(game.image
          ? [
              {
                property: 'og:image',
                content: `${envConfigs.app_url}${game.image}`,
              },
            ]
          : []),
      ],
      links: [
        { rel: 'canonical', href: urlFor(locale) },
        ...locales.map((loc) => ({
          rel: 'alternate',
          hrefLang: loc,
          href: urlFor(loc),
        })),
        { rel: 'alternate', hrefLang: 'x-default', href: urlFor('en') },
      ],
    };
  },
  component: () => {
    const { game } = Route.useLoaderData();
    return <GameDetail game={game} />;
  },
});

import { createFileRoute } from '@tanstack/react-router';

import { envConfigs } from '@/config';
import { m } from '@/paraglide/messages.js';
import { getLocale, locales, localizeUrl } from '@/paraglide/runtime.js';
import { RadiantHome } from '@/blocks/radiant/home';

export const Route = createFileRoute('/')({
  loader: () => {
    const locale = getLocale();
    return {
      locale,
      title: m['radiant.meta.title']({}, { locale }),
      description: m['radiant.meta.description']({}, { locale }),
    };
  },
  head: ({ loaderData }) => {
    const locale = loaderData?.locale ?? 'en';
    const urlFor = (loc: (typeof locales)[number]) =>
      localizeUrl(envConfigs.app_url + '/', { locale: loc }).href;
    const title = loaderData?.title ?? envConfigs.app_name;
    const description = loaderData?.description ?? envConfigs.app_description;
    return {
      meta: [
        { title },
        { name: 'description', content: description },
        { property: 'og:title', content: title },
        { property: 'og:description', content: description },
        { property: 'og:type', content: 'website' },
        { property: 'og:url', content: urlFor(locale) },
        {
          property: 'og:image',
          content: envConfigs.app_url + '/games/kino.png',
        },
        { name: 'twitter:card', content: 'summary_large_image' },
        { name: 'theme-color', content: '#111311' },
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
  component: RadiantHome,
});

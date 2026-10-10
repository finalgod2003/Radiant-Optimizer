import { envConfigs } from '@/config';
import { locales, localizeUrl } from '@/paraglide/runtime.js';

export function optimizerMetadata(
  data:
    | { title: string; description: string; locale: (typeof locales)[number] }
    | undefined,
  path: string
) {
  const title = data?.title ?? envConfigs.app_name;
  const description = data?.description ?? envConfigs.app_description;
  const urlFor = (locale: (typeof locales)[number]) =>
    localizeUrl(envConfigs.app_url + path, { locale }).href;
  return {
    meta: [
      { title },
      { name: 'description', content: description },
      { property: 'og:title', content: title },
      { property: 'og:description', content: description },
      { property: 'og:type', content: 'website' },
      { property: 'og:url', content: urlFor(data?.locale ?? 'en') },
      { name: 'twitter:card', content: 'summary' },
      { name: 'theme-color', content: '#101310' },
    ],
    links: [
      { rel: 'canonical', href: urlFor(data?.locale ?? 'en') },
      ...locales.map((locale) => ({
        rel: 'alternate',
        hrefLang: locale,
        href: urlFor(locale),
      })),
      { rel: 'alternate', hrefLang: 'x-default', href: urlFor('en') },
    ],
  };
}

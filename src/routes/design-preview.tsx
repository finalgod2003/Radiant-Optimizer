import { createFileRoute } from '@tanstack/react-router';

import { envConfigs } from '@/config';
import { m } from '@/paraglide/messages.js';
import { getLocale } from '@/paraglide/runtime.js';
import { DesignPreviews } from '@/blocks/radiant/design-previews';

export const Route = createFileRoute('/design-preview')({
  validateSearch: (
    search: Record<string, unknown>
  ): { style: 'paper' | 'atlas' | 'cinema' | 'overview' } => ({
    style:
      search.style === 'paper' ||
      search.style === 'atlas' ||
      search.style === 'cinema'
        ? search.style
        : 'overview',
  }),
  loader: () => ({
    title: m['radiant.preview.overviewtitle'](
      { product: envConfigs.app_name },
      { locale: getLocale() }
    ),
  }),
  head: ({ loaderData }) => ({
    meta: [
      { title: loaderData?.title ?? envConfigs.app_name },
      { name: 'robots', content: 'noindex, nofollow' },
    ],
  }),
  component: PreviewPage,
});

function PreviewPage() {
  const { style } = Route.useSearch();
  return <DesignPreviews variant={style} />;
}

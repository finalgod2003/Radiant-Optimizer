import { createFileRoute } from '@tanstack/react-router';
import { games } from '@/data/games';

import { envConfigs } from '@/config';
import { locales, localizeUrl } from '@/paraglide/runtime.js';

const escapeXml = (value: string) =>
  value
    .replaceAll('&', '&amp;')
    .replaceAll('"', '&quot;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;');
export const Route = createFileRoute('/sitemap.xml')({
  server: {
    handlers: {
      GET: () => {
        const paths = ['/', ...games.map((game) => '/games/' + game.id)];
        const entries = paths.flatMap((path) =>
          locales.map((locale) => {
            const urlFor = (loc: (typeof locales)[number]) =>
              escapeXml(
                localizeUrl(envConfigs.app_url + path, { locale: loc }).href
              );
            return [
              '  <url>',
              '    <loc>' + urlFor(locale) + '</loc>',
              ...locales.map(
                (loc) =>
                  '    <xhtml:link rel="alternate" hreflang="' +
                  loc +
                  '" href="' +
                  urlFor(loc) +
                  '"/>'
              ),
              '    <changefreq>weekly</changefreq>',
              '    <priority>' + (path === '/' ? '1.0' : '0.8') + '</priority>',
              '  </url>',
            ].join('\n');
          })
        );
        return new Response(
          [
            '<?xml version="1.0" encoding="UTF-8"?>',
            '<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9" xmlns:xhtml="http://www.w3.org/1999/xhtml">',
            ...entries,
            '</urlset>',
          ].join('\n'),
          { headers: { 'Content-Type': 'application/xml; charset=utf-8' } }
        );
      },
    },
  },
});

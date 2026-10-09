import { existsSync } from 'node:fs';
import { compile } from '@inlang/paraglide-js';

// Nitro can load the server entry before the Vite translation plugin's first
// compilation on a fresh checkout. Ensure its imports exist before Vite boots.
if (
  process.argv.includes('--force') ||
  !existsSync('src/paraglide/server.js')
) {
  await compile({
    project: './project.inlang',
    outdir: './src/paraglide',
    outputStructure: 'message-modules',
    cookieName: 'PARAGLIDE_LOCALE',
    strategy: ['url', 'cookie', 'baseLocale'],
    urlPatterns: [
      {
        pattern: '/api/:path(.*)?',
        localized: [
          ['en', '/api/:path(.*)?'],
          ['zh', '/api/:path(.*)?'],
        ],
      },
      {
        pattern: '/',
        localized: [
          ['zh', '/zh'],
          ['en', '/'],
        ],
      },
      {
        pattern: '/:path(.*)?',
        localized: [
          ['zh', '/zh/:path(.*)?'],
          ['en', '/:path(.*)?'],
        ],
      },
    ],
  });
}

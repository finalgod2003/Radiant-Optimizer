import { games, SOURCE_POST } from '@/data/games';

import { envConfigs } from '@/config';

export function directoryText(full = false) {
  const { app_url, app_name, app_description } = envConfigs;
  return [
    `# ${app_name}`,
    '',
    `> ${app_description}`,
    '',
    'Independent community directory; not the official Radiant Optimizer Windows application.',
    `Game list source: ${SOURCE_POST}`,
    '',
    '## Pages',
    `- [Home](${app_url}/): English game directory and toolkit`,
    `- [Chinese](${app_url}/zh): Chinese game directory and toolkit`,
    `- [Gaming tools](${app_url}/#tools): Frame duration and same-game DPI sensitivity calculators`,
    '',
    '## Games and collections',
    ...games.flatMap((game) => [
      `- [${game.title}](${app_url}/games/${game.id}): ${game.category}; ${game.mode === 'embed' ? 'embedded launch available' : 'original-site launch'}`,
      ...(full
        ? [`  Original host: ${game.url}`, `  Availability note: ${game.note}`]
        : []),
    ]),
    '',
    'Games run on third-party hosts. Some require the visitor’s own game files; availability may change.',
    'Calculators run locally and do not modify system settings. Favorites are stored in the current browser.',
    '',
  ].join('\n');
}

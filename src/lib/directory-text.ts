import { games, SOURCE_POST } from '@/data/games';

import { envConfigs } from '@/config';

export function directoryText(full = false) {
  const { app_url, app_name } = envConfigs;
  return [
    `# ${app_name}`,
    '',
    `> ${app_name}: an independently implemented Windows desktop utility with hardware inspection, experimental reversible settings, and device licensing.`,
    '',
    'Independent implementation; not affiliated with the original Radiant Optimizer product. Version 0.2 Preview adds supported CS2/Fortnite/VALORANT config profiles, GPU sensor monitoring, PresentMon frame capture and a windowed/borderless overlay. Unknown game schemas are blocked. NVIDIA driver controls remain planned.',
    `Game list source: ${SOURCE_POST}`,
    '',
    '## Pages',
    `- [Home](${app_url}/): Windows desktop product overview`,
    `- [Chinese](${app_url}/zh): Chinese product overview`,
    `- [Features](${app_url}/features): Current capabilities, supported configuration formats, sensor limitations, and development roadmap`,
    `- [Pricing](${app_url}/pricing): Free and Premium scope; prices and checkout availability come from the server catalog`,
    `- [Download](${app_url}/download): Actual release package and SHA-256 checksum when published; otherwise package preparation status`,
    `- [Games](${app_url}/games): Community game directory`,
    `- [Gaming tools](${app_url}/tools): Frame duration and same-game DPI sensitivity calculators`,
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
    'Desktop configuration changes require explicit opt-in to experimental modification. Detection, configuration preview, sensors and recovery remain free. Premium adds existing power-plan switching, supported game-profile writes, PresentMon FPS capture/export and overlay; one active device.',
    '',
  ].join('\n');
}

import type { GameCategory, GameNote } from '@/data/games';

import { m } from '@/paraglide/messages.js';

export function categoryLabels(): Record<GameCategory, string> {
  return {
    zombies: m['radiant.category.zombies'](),
    shooter: m['radiant.category.shooter'](),
    action: m['radiant.category.action'](),
    sport: m['radiant.category.sport'](),
    strategy: m['radiant.category.strategy'](),
  };
}

export function gameDescriptions(): Record<string, string> {
  return {
    'kino-der-toten': m['radiant.game.kino'](),
    'black-ops-zombies': m['radiant.game.bo1'](),
    'cs-surf': m['radiant.game.surf'](),
    'black-ops-2': m['radiant.game.bo2'](),
    'quake-3': m['radiant.game.quake3'](),
    diablo: m['radiant.game.diablo'](),
    'moon-zombies': m['radiant.game.moon'](),
    'cheese-cube': m['radiant.game.cheese'](),
    'halo-ce': m['radiant.game.halo'](),
    'skate-3': m['radiant.game.skate'](),
    'quake-1': m['radiant.game.quake1'](),
    'quake-2': m['radiant.game.quake2'](),
    'modern-warfare-2': m['radiant.game.mw2'](),
    'skate-rust': m['radiant.game.rust'](),
    'halo-mobile': m['radiant.game.halomobile'](),
    'pes-6': m['radiant.game.pes'](),
    'gta-5': m['radiant.game.gta5'](),
    'vice-city': m['radiant.game.vice'](),
    'simpsons-hit-run': m['radiant.game.simpsons'](),
    wolfenstein: m['radiant.game.wolf'](),
    'urban-terror': m['radiant.game.urban'](),
    'half-life': m['radiant.game.halflife'](),
    'counter-strike': m['radiant.game.cs'](),
    hedgewars: m['radiant.game.hedge'](),
    'wasm-arcade': m['radiant.game.wasm'](),
  };
}

export function gameNotes(): Record<GameNote, string> {
  return {
    standard: m['radiant.player.standard'](),
    isolation: m['radiant.player.isolation'](),
    blocked: m['radiant.player.blocked'](),
    archive: m['radiant.player.archive'](),
    offline: m['radiant.player.offline'](),
    limited: m['radiant.player.limited'](),
    files: m['radiant.player.files'](),
    'source-label': m['radiant.player.sourcelabel'](),
    collection: m['radiant.player.collection'](),
  };
}

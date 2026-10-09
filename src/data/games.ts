export const SOURCE_POST = 'https://x.com/RadiantOpti/status/2108068632991256995';

export type GameCategory = 'zombies' | 'shooter' | 'action' | 'sport' | 'strategy';
export type GameNote = 'standard' | 'isolation' | 'blocked' | 'archive' | 'offline' | 'limited' | 'files' | 'source-label' | 'collection';
export type Game = {
  id: string;
  title: string;
  category: GameCategory;
  url: string;
  image?: string;
  accent: string;
  monogram: string;
  mode: 'embed' | 'external';
  note: GameNote;
};

// Destinations transcribed from the source post. Availability was checked on
// 2026-10-09; remote hosts can change independently of this directory.
export const games: Game[] = [
  { id: 'kino-der-toten', title: 'Kino der Toten', category: 'zombies', url: 'https://kino-der-toten.pages.dev/', image: '/games/kino.png', accent: '#d8ac60', monogram: 'KDT', mode: 'embed', note: 'standard' },
  { id: 'black-ops-zombies', title: 'Black Ops Zombies', category: 'zombies', url: 'https://vel.gg/bo1z', image: '/games/bo1.png', accent: '#ba8c58', monogram: 'BO1', mode: 'external', note: 'isolation' },
  { id: 'cs-surf', title: 'CS Surf', category: 'shooter', url: 'https://surfd.net/', image: '/games/surf.png', accent: '#81cbd2', monogram: 'SURF', mode: 'embed', note: 'standard' },
  { id: 'black-ops-2', title: 'Black Ops 2', category: 'shooter', url: 'https://vibeslops.luckeysystems.com/', image: '/games/bo2.png', accent: '#ec8c4c', monogram: 'BO2', mode: 'embed', note: 'standard' },
  { id: 'quake-3', title: 'Quake III Arena', category: 'shooter', url: 'https://q3.pieter.com/', image: '/games/quake3.jpg', accent: '#c15c50', monogram: 'Q III', mode: 'embed', note: 'standard' },
  { id: 'diablo', title: 'Diablo', category: 'action', url: 'https://johnimril.github.io/diablo_web/', image: '/games/diablo.png', accent: '#ba5947', monogram: 'DIABLO', mode: 'embed', note: 'files' },
  { id: 'moon-zombies', title: 'Moon Zombies', category: 'zombies', url: 'https://moon-zombies.pages.dev/', accent: '#a6b7da', monogram: 'MOON', mode: 'embed', note: 'standard' },
  { id: 'cheese-cube', title: 'BO3 Cheese Cube', category: 'zombies', url: 'https://cheese-cube.pages.dev/', accent: '#e9c864', monogram: 'CUBE', mode: 'embed', note: 'standard' },
  { id: 'halo-ce', title: 'Halo: Combat Evolved', category: 'shooter', url: 'https://mitchellhynes.com/halo', accent: '#9ccda5', monogram: 'HALO', mode: 'external', note: 'isolation' },
  { id: 'skate-3', title: 'Skate 3', category: 'sport', url: 'https://skate.aaddpp.lol/', accent: '#ccdf9b', monogram: 'SKATE', mode: 'embed', note: 'standard' },
  { id: 'quake-1', title: 'QuakeWorld', category: 'shooter', url: 'https://q1.pieter.com/', image: '/games/quake1.jpg', accent: '#c69d74', monogram: 'Q I', mode: 'embed', note: 'standard' },
  { id: 'quake-2', title: 'Quake II', category: 'shooter', url: 'https://q2.pieter.com/', image: '/games/quake2.jpg', accent: '#c99864', monogram: 'Q II', mode: 'embed', note: 'standard' },
  { id: 'modern-warfare-2', title: 'Modern Warfare 2', category: 'shooter', url: 'https://ovz-game-production.up.railway.app/', accent: '#8ebc79', monogram: 'MW2', mode: 'external', note: 'limited' },
  { id: 'skate-rust', title: 'Skate Rust', category: 'sport', url: 'https://global-terror.net/', accent: '#b6a5d3', monogram: 'RUST', mode: 'external', note: 'blocked' },
  { id: 'halo-mobile', title: 'Halo CE Mobile', category: 'shooter', url: 'https://hcemobile.com/', accent: '#87c5af', monogram: 'HALO M', mode: 'embed', note: 'standard' },
  { id: 'pes-6', title: 'PES 6', category: 'sport', url: 'https://pes6.optijuegos.net/', accent: '#83cdb0', monogram: 'PES 6', mode: 'embed', note: 'standard' },
  { id: 'gta-5', title: 'GTA 5 · Archive', category: 'action', url: 'https://web.archive.org/web/20261006055917/https://playgta5.com/', accent: '#91b994', monogram: 'GTA V', mode: 'external', note: 'archive' },
  { id: 'vice-city', title: 'GTA: Vice City', category: 'action', url: 'https://joncodeofficial.github.io/gta-vice-city-wasm', accent: '#d28db5', monogram: 'VICE', mode: 'embed', note: 'files' },
  { id: 'simpsons-hit-run', title: 'The Simpsons: Hit & Run', category: 'action', url: 'https://shar-wasm.cjoseph.workers.dev/?skipmovie', accent: '#e3cf74', monogram: 'HIT & RUN', mode: 'external', note: 'isolation' },
  { id: 'wolfenstein', title: 'Return to Castle Wolfenstein', category: 'shooter', url: 'https://rtcw.pieter.com/', image: '/games/wolfenstein.jpg', accent: '#b9a18a', monogram: 'RTCW', mode: 'embed', note: 'standard' },
  { id: 'urban-terror', title: 'Urban Terror', category: 'shooter', url: 'https://ut.pieter.com/', image: '/games/urban-terror.jpg', accent: '#bca67b', monogram: 'URBAN', mode: 'embed', note: 'source-label' },
  { id: 'half-life', title: 'Half-Life', category: 'shooter', url: 'https://pixelsuft.github.io/hl/', accent: '#d68e51', monogram: 'λ', mode: 'embed', note: 'files' },
  { id: 'counter-strike', title: 'Half-Life / CS 1.6', category: 'shooter', url: 'https://x8bitrain.github.io/webXash/', accent: '#b8b996', monogram: 'CS 1.6', mode: 'embed', note: 'files' },
  { id: 'hedgewars', title: 'Hedgewars', category: 'strategy', url: 'https://webwars.link/', accent: '#d7a5af', monogram: 'HEDGE', mode: 'external', note: 'offline' },
  { id: 'wasm-arcade', title: 'WASM Arcade', category: 'action', url: 'https://wasmarcade.com/Fan', accent: '#9eaae6', monogram: 'WASM', mode: 'external', note: 'collection' },
];

export function getGame(id: string) {
  return games.find((game) => game.id === id);
}

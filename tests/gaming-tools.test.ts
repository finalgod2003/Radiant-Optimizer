import assert from 'node:assert/strict';
import test from 'node:test';

import { games } from '../src/data/games';
import { convertSensitivity, frameTime } from '../src/lib/gaming-tools';

test('frame duration uses milliseconds and allows fractional refresh rates', () => {
  assert.equal(frameTime(60), 1000 / 60);
  assert.equal(frameTime(240), 1000 / 240);
  assert.equal(frameTime(59.94), 1000 / 59.94);
});
test('invalid frame rates never display NaN or Infinity', () => {
  for (const value of [0, -60, NaN, Infinity, Number.MIN_VALUE])
    assert.equal(frameTime(value), null);
});
test('DPI conversion preserves effective sensitivity and is reversible', () => {
  assert.equal(convertSensitivity(0.4, 800, 1600), 0.2);
  assert.equal(convertSensitivity(0.2, 1600, 800), 0.4);
  assert.equal(convertSensitivity(1.5, 800, 800), 1.5);
});
test('invalid DPI inputs and overflow are rejected', () => {
  for (const bad of [0, -1, NaN, Infinity]) {
    assert.equal(convertSensitivity(bad, 800, 1600), null);
    assert.equal(convertSensitivity(0.4, bad, 1600), null);
    assert.equal(convertSensitivity(0.4, 800, bad), null);
  }
  assert.equal(convertSensitivity(Number.MAX_VALUE, 2, 1), null);
});
test('the complete catalog has distinct paths, HTTPS destinations and safe restrictions', () => {
  assert.equal(games.length, 25);
  assert.equal(new Set(games.map((game) => game.id)).size, 25);
  for (const game of games) {
    assert.match(game.id, /^[a-z0-9-]+$/);
    assert.equal(new URL(game.url).protocol, 'https:');
    if (
      ['blocked', 'isolation', 'collection', 'archive', 'offline'].includes(
        game.note
      )
    )
      assert.equal(game.mode, 'external');
  }
});

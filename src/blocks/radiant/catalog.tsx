import { useMemo, useState } from 'react';
import { games, type GameCategory } from '@/data/games';
import {
  ArrowDown,
  Bookmark,
  Grid2X2,
  Search,
  SlidersHorizontal,
} from 'lucide-react';

import { m } from '@/paraglide/messages.js';
import { useSavedGames } from '@/hooks/use-saved-games';
import { GameCard } from '@/components/radiant/game-card';

import { categoryLabels, gameDescriptions } from './content';

export function GameCatalog() {
  const [query, setQuery] = useState('');
  const [category, setCategory] = useState<GameCategory | 'all'>('all');
  const [onlySaved, setOnlySaved] = useState(false);
  const [sort, setSort] = useState('featured');
  const [expanded, setExpanded] = useState(false);
  const { saved, toggle } = useSavedGames();
  const categories = categoryLabels();
  const descriptions = gameDescriptions();
  const filtered = useMemo(() => {
    const q = query.trim().toLocaleLowerCase();
    const result = games.filter(
      (game) =>
        (category === 'all' || game.category === category) &&
        (!onlySaved || saved.includes(game.id)) &&
        (!q ||
          `${game.title} ${game.id} ${descriptions[game.id]} ${categories[game.category]}`
            .toLocaleLowerCase()
            .includes(q))
    );
    return sort === 'az'
      ? result.sort((a, b) => a.title.localeCompare(b.title))
      : result;
  }, [query, category, onlySaved, saved, sort, descriptions, categories]);
  const shown =
    expanded || query || category !== 'all' || onlySaved
      ? filtered
      : filtered.slice(0, 8);
  return (
    <section className="catalog-section site-width" id="library">
      <div className="section-heading">
        <div>
          <p className="eyebrow">
            <span className="section-number">01 /</span>
            {m['radiant.catalog.eyebrow']()}
          </p>
          <h2>{m['radiant.catalog.title']()}</h2>
          <p className="section-description">
            {m['radiant.catalog.description']()}
          </p>
        </div>
        <label className="search-box">
          <Search size={18} />
          <input
            type="search"
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            placeholder={m['radiant.catalog.search']()}
            aria-label={m['radiant.catalog.searchlabel']()}
          />
          <span className="search-key" aria-hidden="true">
            ⌕
          </span>
        </label>
      </div>
      <div className="catalog-toolbar">
        <div
          className="filter-chips"
          aria-label={m['radiant.catalog.searchlabel']()}
        >
          <button
            className={category === 'all' && !onlySaved ? 'active' : ''}
            onClick={() => {
              setCategory('all');
              setOnlySaved(false);
            }}
            aria-pressed={category === 'all' && !onlySaved}
          >
            <Grid2X2 size={14} />
            {m['radiant.catalog.all']()}
            <span>{games.length}</span>
          </button>
          {(Object.keys(categories) as GameCategory[]).map((key) => (
            <button
              className={category === key ? 'active' : ''}
              key={key}
              aria-pressed={category === key}
              onClick={() => {
                setCategory(key);
                setOnlySaved(false);
              }}
            >
              {categories[key]}
            </button>
          ))}
          <button
            className={`saved-filter ${onlySaved ? 'active' : ''}`}
            aria-pressed={onlySaved}
            onClick={() => {
              setOnlySaved(!onlySaved);
              setCategory('all');
            }}
          >
            <Bookmark size={14} />
            {m['radiant.catalog.saved']()}
            {saved.length > 0 && <span>{saved.length}</span>}
          </button>
        </div>
        <label className="sort-control">
          <SlidersHorizontal size={14} />
          <select
            aria-label={m['radiant.catalog.sort']()}
            value={sort}
            onChange={(e) => setSort(e.target.value)}
          >
            <option value="featured">{m['radiant.catalog.featured']()}</option>
            <option value="az">{m['radiant.catalog.az']()}</option>
          </select>
        </label>
      </div>
      <div className="result-summary" aria-live="polite">
        {m['radiant.catalog.results']({ count: filtered.length })}
      </div>
      {shown.length ? (
        <div className="game-grid">
          {shown.map((game) => (
            <GameCard
              key={game.id}
              game={game}
              description={descriptions[game.id]}
              category={categories[game.category]}
              saved={saved.includes(game.id)}
              onToggle={() => toggle(game.id)}
              saveLabel={
                saved.includes(game.id)
                  ? m['radiant.catalog.unsave']({ title: game.title })
                  : m['radiant.catalog.save']({ title: game.title })
              }
              playLabel={m['radiant.catalog.play']()}
              externalLabel={m['radiant.catalog.external']()}
            />
          ))}
        </div>
      ) : (
        <div className="catalog-empty">
          <Search size={32} />
          <h3>{m['radiant.catalog.empty']()}</h3>
          <p>{m['radiant.catalog.emptydesc']()}</p>
          <button
            className="button button-secondary"
            onClick={() => {
              setQuery('');
              setCategory('all');
              setOnlySaved(false);
            }}
          >
            {m['radiant.catalog.clear']()}
          </button>
        </div>
      )}
      {!expanded && shown.length < filtered.length && (
        <div className="load-more">
          <button
            className="button button-secondary"
            onClick={() => setExpanded(true)}
          >
            {m['radiant.catalog.showall']({ count: filtered.length })}
            <ArrowDown size={16} />
          </button>
        </div>
      )}
      <p className="catalog-footnote">{m['radiant.catalog.note']()}</p>
    </section>
  );
}

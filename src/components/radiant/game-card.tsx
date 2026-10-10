import { useState, type CSSProperties } from 'react';
import type { Game } from '@/data/games';
import { ArrowUpRight, Bookmark, Play } from 'lucide-react';

import { Link } from '@/core/i18n/navigation';

export function GameArtwork({
  game,
  eager = false,
}: {
  game: Game;
  eager?: boolean;
}) {
  const [failed, setFailed] = useState(false);
  return (
    <div
      className="game-artwork"
      style={{ '--game-accent': game.accent } as CSSProperties}
    >
      {game.image && !failed ? (
        <img
          src={game.image}
          alt=""
          loading={eager ? 'eager' : 'lazy'}
          onError={() => setFailed(true)}
          width={640}
          height={360}
        />
      ) : (
        <div className="game-type-art" aria-hidden="true">
          <span className="art-cross">+</span>
          <strong>{game.monogram}</strong>
          <span className="art-index">
            {game.id.replaceAll('-', ' ').toUpperCase()}
          </span>
        </div>
      )}
    </div>
  );
}

export function GameCard({
  game,
  description,
  category,
  saved,
  onToggle,
  saveLabel,
  playLabel,
  externalLabel,
}: {
  game: Game;
  description: string;
  category: string;
  saved: boolean;
  onToggle: () => void;
  saveLabel: string;
  playLabel: string;
  externalLabel: string;
}) {
  return (
    <article className="game-card">
      <Link
        href={`/games/${game.id}`}
        className="game-cover-link"
        aria-label={`${playLabel}: ${game.title}`}
      >
        <GameArtwork game={game} />
        <span className="card-play" aria-hidden="true">
          <Play size={20} fill="currentColor" />
        </span>
        {game.mode === 'external' && (
          <span className="card-external">
            <ArrowUpRight size={12} />
            {externalLabel}
          </span>
        )}
      </Link>
      <button
        className={`save-game ${saved ? 'is-saved' : ''}`}
        aria-label={saveLabel}
        aria-pressed={saved}
        onClick={onToggle}
      >
        <Bookmark size={16} fill={saved ? 'currentColor' : 'none'} />
      </button>
      <div className="game-card-body">
        <div className="game-meta">
          <span>{category}</span>
          <span className="mini-cross">↗</span>
        </div>
        <h3>
          <Link href={`/games/${game.id}`}>{game.title}</Link>
        </h3>
        <p>{description}</p>
      </div>
    </article>
  );
}

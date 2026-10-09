import { games, SOURCE_POST, type Game } from '@/data/games';
import { ArrowLeft, ArrowUpRight, Info } from 'lucide-react';

import { Link } from '@/core/i18n/navigation';
import { m } from '@/paraglide/messages.js';
import { useSavedGames } from '@/hooks/use-saved-games';
import { GameCard } from '@/components/radiant/game-card';
import { GamePlayer } from '@/components/radiant/game-player';

import { categoryLabels, gameDescriptions, gameNotes } from './content';
import { RadiantShell } from './shell';

export function GameDetail({ game }: { game: Game }) {
  const categories = categoryLabels();
  const descriptions = gameDescriptions();
  const notes = gameNotes();
  const { saved, toggle } = useSavedGames();
  const related = games
    .filter((item) => item.category === game.category && item.id !== game.id)
    .slice(0, 4);
  return (
    <RadiantShell>
      <div className="detail-page site-width">
        <Link href="/#library" className="back-link">
          <ArrowLeft size={16} />
          {m['radiant.player.back']()}
        </Link>
        <div className="detail-heading">
          <div>
            <p className="eyebrow">
              {m['radiant.player.eyebrow']()}
              <span className="eyebrow-separator">/</span>
              {categories[game.category]}
            </p>
            <h1>{game.title}</h1>
            <p>{descriptions[game.id]}</p>
          </div>
          <span className="detail-tag">
            {game.mode === 'embed'
              ? m['radiant.catalog.play']()
              : m['radiant.catalog.external']()}
          </span>
        </div>
        <GamePlayer
          key={game.id}
          game={game}
          labels={{
            launch: m['radiant.player.launch'](),
            reload: m['radiant.player.reload'](),
            stop: m['radiant.player.stop'](),
            fullscreen: m['radiant.player.fullscreen'](),
            exitFullscreen: m['radiant.player.exitfullscreen'](),
            original: m['radiant.player.original'](),
            hint: m['radiant.player.hint'](),
            loading: m['radiant.player.loading'](),
            slow: m['radiant.player.slow'](),
            fullscreenError: m['radiant.player.fullscreenerror'](),
            before: m['radiant.player.before'](),
            privacy: m['radiant.player.privacy'](),
            note: notes[game.note],
            visit: m['radiant.player.visit'](),
          }}
        />
        <div className="game-info-grid">
          <section>
            <h2>{m['radiant.player.about']()}</h2>
            <p>{descriptions[game.id]}</p>
            <p className="info-note">
              <Info size={16} />
              {notes[game.note]}
            </p>
          </section>
          <section>
            <h2>{m['radiant.player.source']()}</h2>
            <a
              href={game.url}
              target="_blank"
              rel="noopener noreferrer"
              className="text-link"
            >
              {new URL(game.url).hostname}
              <ArrowUpRight size={15} />
            </a>
            <a
              href={SOURCE_POST}
              target="_blank"
              rel="noopener noreferrer"
              className="text-link"
            >
              {m['radiant.player.sourcepost']()}
              <ArrowUpRight size={15} />
            </a>
            <p className="last-checked">{m['radiant.player.checked']()}</p>
          </section>
        </div>
        {related.length > 0 && (
          <section className="related-section">
            <div className="section-heading">
              <h2>{m['radiant.player.more']()}</h2>
              <Link href="/#library" className="text-link">
                {m['radiant.nav.library']()}
                <ArrowUpRight size={16} />
              </Link>
            </div>
            <div className="game-grid">
              {related.map((item) => (
                <GameCard
                  key={item.id}
                  game={item}
                  description={descriptions[item.id]}
                  category={categories[item.category]}
                  saved={saved.includes(item.id)}
                  onToggle={() => toggle(item.id)}
                  saveLabel={
                    saved.includes(item.id)
                      ? m['radiant.catalog.unsave']({ title: item.title })
                      : m['radiant.catalog.save']({ title: item.title })
                  }
                  playLabel={m['radiant.catalog.play']()}
                  externalLabel={m['radiant.catalog.external']()}
                />
              ))}
            </div>
          </section>
        )}
      </div>
    </RadiantShell>
  );
}

import { games, SOURCE_POST } from '@/data/games';
import {
  ArrowDownRight,
  ArrowRight,
  ArrowUpRight,
  Check,
  ChevronDown,
  Crosshair,
  Play,
} from 'lucide-react';

import { Link } from '@/core/i18n/navigation';
import { envConfigs } from '@/config';
import { m } from '@/paraglide/messages.js';
import { GameArtwork } from '@/components/radiant/game-card';

import { GameCatalog } from './catalog';
import { RadiantShell } from './shell';
import { GamingTools } from './tools';

export function RadiantHome() {
  const [first, ...rest] = envConfigs.app_name.split(' ');
  const faqs = [
    [m['radiant.about.q1'](), m['radiant.about.a1']()],
    [m['radiant.about.q2'](), m['radiant.about.a2']()],
    [m['radiant.about.q3'](), m['radiant.about.a3']()],
    [m['radiant.about.q4'](), m['radiant.about.a4']()],
  ];
  return (
    <RadiantShell home>
      <section className="hero-section site-width">
        <div className="hero-copy">
          <p className="eyebrow">
            <span className="status-dot" />
            {m['radiant.hero.eyebrow']()}
          </p>
          <h1>
            <span>{first}</span>{' '}
            <span className="accent-text">{rest.join(' ')}</span>
            <Crosshair className="hero-crosshair" aria-hidden="true" />
          </h1>
          <p className="hero-description">{m['radiant.hero.description']()}</p>
          <div className="hero-actions">
            <a className="button button-primary" href="#library">
              {m['radiant.hero.explore']()}
              <ArrowDownRight size={19} />
            </a>
            <a className="hero-tool-link" href="#tools">
              {m['radiant.hero.tools']()}
              <ArrowUpRight size={16} />
            </a>
          </div>
          <div className="hero-reassurance">
            <span>
              <Check size={13} />
              {m['radiant.hero.noaccount']()}
            </span>
            <span>
              <Check size={13} />
              {m['radiant.hero.instant']()}
            </span>
          </div>
        </div>
        <Link
          href="/games/kino-der-toten"
          className="hero-feature"
          aria-label={m['radiant.hero.play']()}
        >
          <GameArtwork game={games[0]} eager />
          <div className="feature-top">
            <span>
              <span className="status-dot" />
              {m['radiant.hero.featured']()}
            </span>
            <span>01 — 25</span>
          </div>
          <span className="feature-play">
            <Play size={26} fill="currentColor" />
          </span>
          <div className="feature-bottom">
            <div>
              <span className="feature-genre">
                {m['radiant.category.zombies']()} / WEB
              </span>
              <h2>Kino der Toten</h2>
              <p>{m['radiant.hero.featuredesc']()}</p>
            </div>
            <span className="feature-arrow">
              <ArrowUpRight size={22} />
            </span>
          </div>
        </Link>
      </section>
      <div className="stats-strip site-width">
        <a
          href={SOURCE_POST}
          className="curated-by"
          target="_blank"
          rel="noopener noreferrer"
        >
          <span className="curated-icon">𝕏</span>
          <span>
            {m['radiant.hero.curated']()}
            <strong>
              @RadiantOpti
              <ArrowUpRight size={13} />
            </strong>
          </span>
        </a>
        <div className="hero-stat">
          <strong>{games.length}</strong>
          <span>{m['radiant.hero.destinations']()}</span>
        </div>
        <div className="hero-stat">
          <strong>
            {games.filter((game) => game.mode === 'embed').length}
          </strong>
          <span>{m['radiant.hero.embeds']()}</span>
        </div>
        <div className="hero-stat">
          <strong>02</strong>
          <span>{m['radiant.hero.freetools']()}</span>
        </div>
        <ArrowDownRight className="stats-arrow" size={30} />
      </div>
      <GameCatalog />
      <GamingTools />
      <section id="about" className="about-section site-width">
        <div>
          <p className="eyebrow">
            <span className="section-number">03 /</span>
            {m['radiant.about.eyebrow']()}
          </p>
          <h2>{m['radiant.about.title']()}</h2>
          <a
            href={SOURCE_POST}
            className="text-link"
            target="_blank"
            rel="noopener noreferrer"
          >
            {m['radiant.player.sourcepost']()}
            <ArrowRight size={16} />
          </a>
        </div>
        <div className="faq-list">
          {faqs.map(([q, a]) => (
            <details key={q}>
              <summary>
                {q}
                <ChevronDown size={18} />
              </summary>
              <p>{a}</p>
            </details>
          ))}
        </div>
      </section>
    </RadiantShell>
  );
}

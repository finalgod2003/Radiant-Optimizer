import { ArrowLeft } from 'lucide-react';

import { Link } from '@/core/i18n/navigation';
import { envConfigs } from '@/config';
import { m } from '@/paraglide/messages.js';

import { GameCatalog } from './catalog';
import { RadiantShell } from './shell';
import { GamingTools } from './tools';

export function RadiantGames() {
  return (
    <RadiantShell home>
      <section className="games-intro site-width">
        <Link href="/" className="back-link">
          <ArrowLeft size={16} /> {envConfigs.app_name}
        </Link>
        <p className="eyebrow">{m['radiant.product.gameskicker']()}</p>
        <h1>{m['radiant.product.navgames']()}</h1>
        <p>{m['radiant.games.description']()}</p>
      </section>
      <GameCatalog />
      <GamingTools />
    </RadiantShell>
  );
}

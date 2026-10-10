import { ArrowLeft } from 'lucide-react';

import { Link } from '@/core/i18n/navigation';
import { m } from '@/paraglide/messages.js';
import { GameCatalog } from '@/blocks/radiant/catalog';
import { RadiantShell } from '@/blocks/radiant/shell';
import { GamingTools } from '@/blocks/radiant/tools';

export function OptimizerGames() {
  return (
    <RadiantShell>
      <div className="detail-page site-width" style={{ paddingBottom: 0 }}>
        <Link href="/" className="back-link">
          <ArrowLeft size={16} />
          {m['optimizer.library.back']()}
        </Link>
        <div className="detail-heading">
          <div>
            <p className="eyebrow">{m['optimizer.library.games_eyebrow']()}</p>
            <h1>{m['optimizer.library.games_title']()}</h1>
            <p>{m['optimizer.library.games_description']()}</p>
          </div>
        </div>
      </div>
      <GameCatalog />
    </RadiantShell>
  );
}

export function OptimizerTools() {
  return (
    <RadiantShell>
      <div className="detail-page site-width" style={{ paddingBottom: 0 }}>
        <Link href="/" className="back-link">
          <ArrowLeft size={16} />
          {m['optimizer.library.back']()}
        </Link>
        <div className="detail-heading">
          <div>
            <p className="eyebrow">{m['optimizer.library.tools_eyebrow']()}</p>
            <h1>{m['optimizer.library.tools_title']()}</h1>
            <p>{m['optimizer.library.tools_description']()}</p>
          </div>
        </div>
      </div>
      <GamingTools />
    </RadiantShell>
  );
}

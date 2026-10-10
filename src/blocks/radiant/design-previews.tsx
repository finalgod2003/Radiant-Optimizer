import { ArrowRight, ArrowUpRight } from 'lucide-react';

import { Link } from '@/core/i18n/navigation';
import { envConfigs } from '@/config';
import { m } from '@/paraglide/messages.js';

import { ProductPage, type PreviewStyle } from './product';

type PreviewVariant = PreviewStyle | 'overview';

function previewOptions() {
  return [
    {
      id: 'paper',
      letter: 'A',
      name: m['radiant.preview.paper'](),
      description: m['radiant.preview.paperdesc'](),
    },
    {
      id: 'atlas',
      letter: 'B',
      name: m['radiant.preview.atlas'](),
      description: m['radiant.preview.atlasdesc'](),
    },
    {
      id: 'cinema',
      letter: 'C',
      name: m['radiant.preview.cinema'](),
      description: m['radiant.preview.cinemadesc'](),
    },
  ] as const;
}

function PreviewBar({ variant }: { variant: PreviewVariant }) {
  return (
    <div className="pv-switcher">
      <Link className="pv-switcher-home" href="/design-preview">
        {m['radiant.preview.title']()}
      </Link>
      <nav aria-label={m['radiant.preview.switch']()}>
        {previewOptions().map((option) => (
          <Link
            key={option.id}
            href={`/design-preview?style=${option.id}`}
            aria-current={variant === option.id ? 'page' : undefined}
          >
            <span>{option.letter}</span>
            {option.name}
          </Link>
        ))}
      </nav>
      <span className="pv-prototype-note">{m['radiant.preview.status']()}</span>
    </div>
  );
}

function PreviewOverview() {
  return (
    <main className="pv-overview">
      <div className="pv-overview-intro">
        <p>{m['radiant.preview.status']()}</p>
        <h1>
          {m['radiant.preview.overviewtitle']({ product: envConfigs.app_name })}
        </h1>
        <p>{m['radiant.preview.overviewdesc']()}</p>
      </div>
      <ol className="pv-structure">
        {[
          m['radiant.product.structure1'](),
          m['radiant.product.structure2'](),
          m['radiant.product.structure3'](),
          m['radiant.product.structure4'](),
        ].map((step) => (
          <li key={step}>
            {step}
            <ArrowRight size={15} />
          </li>
        ))}
      </ol>
      <div className="pv-study-grid">
        {previewOptions().map((option) => (
          <Link
            key={option.id}
            className={`pv-study pv-study-${option.id}`}
            href={`/design-preview?style=${option.id}`}
          >
            <div className="pv-study-image">
              <img
                src={`/design-preview/${option.id}-client-desktop.png`}
                alt={option.name}
                width={1440}
                height={1000}
              />
            </div>
            <div className="pv-study-caption">
              <span>{option.letter}</span>
              <div>
                <h2>{option.name}</h2>
                <p>{option.description}</p>
              </div>
              <ArrowUpRight size={22} />
            </div>
          </Link>
        ))}
      </div>
      <p className="pv-overview-help">{m['radiant.preview.choose']()}</p>
      <p className="pv-overview-note">{m['radiant.product.previewnote']()}</p>
    </main>
  );
}

export function DesignPreviews({ variant }: { variant: PreviewVariant }) {
  return (
    <div className="design-previews">
      <PreviewBar variant={variant} />
      {variant === 'overview' ? (
        <PreviewOverview />
      ) : (
        <ProductPage key={variant} variant={variant} />
      )}
    </div>
  );
}

import { ArrowRight, Check, Clock3, ShieldCheck } from 'lucide-react';

import { Link } from '@/core/i18n/navigation';
import { m } from '@/paraglide/messages.js';
import {
  FeatureList,
  PageIntro,
  ProductCta,
  SupportMatrix,
} from '@/components/optimizer/product-sections';

import { optimizerFeatures } from './content';
import { OptimizerShell } from './shell';

export function OptimizerFeatures() {
  const roadmap = [
    {
      title: m['optimizer.roadmap.games.title'](),
      description: m['optimizer.roadmap.games.description'](),
    },
    {
      title: m['optimizer.roadmap.network.title'](),
      description: m['optimizer.roadmap.network.description'](),
    },
    {
      title: m['optimizer.roadmap.monitoring.title'](),
      description: m['optimizer.roadmap.monitoring.description'](),
    },
  ];
  return (
    <OptimizerShell>
      <PageIntro
        eyebrow={m['optimizer.features.eyebrow']()}
        title={m['optimizer.features.title']()}
        description={m['optimizer.features.description']()}
      >
        <div className="op-actions">
          <Link href="/download" className="op-button">
            {m['optimizer.nav.download']()}
            <ArrowRight size={17} />
          </Link>
          <span className="op-small-note">
            {m['optimizer.features.compatibility']()}
          </span>
        </div>
      </PageIntro>
      <section className="op-width op-section op-section-tight">
        <div className="op-section-heading">
          <div>
            <p className="op-eyebrow">01 / {m['optimizer.features.scope']()}</p>
            <h2>{m['optimizer.features.scope_title']()}</h2>
          </div>
          <span className="op-badge">
            <Check size={13} />
            {m['optimizer.features.preview_badge']()}
          </span>
        </div>
        <FeatureList items={optimizerFeatures()} />
      </section>
      <section
        className="op-width op-section op-section-tight"
        id="compatibility"
      >
        <div className="op-section-heading">
          <div>
            <p className="op-eyebrow">
              02 / {m['optimizer.support.eyebrow']()}
            </p>
            <h2>{m['optimizer.support.title']()}</h2>
          </div>
          <p>{m['optimizer.support.description']()}</p>
        </div>
        <SupportMatrix
          title={m['optimizer.support.games.title']()}
          columns={[
            m['optimizer.support.games.game'](),
            m['optimizer.support.games.format'](),
            m['optimizer.support.games.scope'](),
          ]}
          rows={[
            {
              name: 'Counter-Strike 2',
              source: m['optimizer.support.games.cs2.source'](),
              scope: m['optimizer.support.games.cs2.scope'](),
            },
            {
              name: 'Fortnite',
              source: m['optimizer.support.games.fortnite.source'](),
              scope: m['optimizer.support.games.fortnite.scope'](),
            },
            {
              name: 'VALORANT',
              source: m['optimizer.support.games.valorant.source'](),
              scope: m['optimizer.support.games.valorant.scope'](),
            },
          ]}
          note={m['optimizer.support.games.note']()}
        />
        <SupportMatrix
          title={m['optimizer.support.monitoring.title']()}
          columns={[
            m['optimizer.support.monitoring.metric'](),
            m['optimizer.support.monitoring.source'](),
            m['optimizer.support.monitoring.condition'](),
          ]}
          rows={[
            {
              name: m['optimizer.support.monitoring.system.name'](),
              source: m['optimizer.support.monitoring.system.source'](),
              scope: m['optimizer.support.monitoring.system.scope'](),
            },
            {
              name: m['optimizer.support.monitoring.nvidia.name'](),
              source: m['optimizer.support.monitoring.nvidia.source'](),
              scope: m['optimizer.support.monitoring.nvidia.scope'](),
            },
            {
              name: m['optimizer.support.monitoring.other.name'](),
              source: m['optimizer.support.monitoring.other.source'](),
              scope: m['optimizer.support.monitoring.other.scope'](),
            },
            {
              name: m['optimizer.support.monitoring.frames.name'](),
              source: m['optimizer.support.monitoring.frames.source'](),
              scope: m['optimizer.support.monitoring.frames.scope'](),
            },
          ]}
          note={m['optimizer.support.monitoring.note']()}
        />
      </section>
      <section className="op-safety op-width">
        <ShieldCheck size={36} strokeWidth={1.4} />
        <div>
          <h2>{m['optimizer.features.safety_title']()}</h2>
          <p>{m['optimizer.features.safety_description']()}</p>
          <Link href="/pricing" className="op-text-link">
            {m['optimizer.home.compare']()}
            <ArrowRight size={16} />
          </Link>
        </div>
      </section>
      <section className="op-section op-width" id="roadmap">
        <div className="op-section-heading">
          <div>
            <p className="op-eyebrow">
              03 / {m['optimizer.roadmap.eyebrow']()}
            </p>
            <h2>{m['optimizer.roadmap.title']()}</h2>
          </div>
          <p>{m['optimizer.roadmap.description']()}</p>
        </div>
        <div className="op-roadmap-grid">
          {roadmap.map((item) => (
            <article key={item.title}>
              <span className="op-badge op-badge-muted">
                <Clock3 size={13} />
                {m['optimizer.roadmap.planned']()}
              </span>
              <h3>{item.title}</h3>
              <p>{item.description}</p>
            </article>
          ))}
        </div>
      </section>
      <ProductCta
        title={m['optimizer.cta.title']()}
        description={m['optimizer.cta.description']()}
        download={m['optimizer.nav.download']()}
        features={m['optimizer.hero.features']()}
      />
    </OptimizerShell>
  );
}

import {
  ArrowDownToLine,
  ArrowRight,
  ArrowUpRight,
  Check,
  Eye,
  History,
  ScanLine,
} from 'lucide-react';

import { Link } from '@/core/i18n/navigation';
import { envConfigs } from '@/config';
import { m } from '@/paraglide/messages.js';
import {
  FaqList,
  FeatureList,
  ProductCta,
  WorkflowPreview,
} from '@/components/optimizer/product-sections';

import { useOptimizerCatalog } from './catalog-query';
import { optimizerFaqs, optimizerFeatures } from './content';
import { OptimizerShell } from './shell';

export function OptimizerHome() {
  const catalog = useOptimizerCatalog();
  const steps = [
    {
      title: m['optimizer.workflow.inspect.title'](),
      description: m['optimizer.workflow.inspect.description'](),
      status: m['optimizer.workflow.inspect.status'](),
      icon: ScanLine,
    },
    {
      title: m['optimizer.workflow.preview.title'](),
      description: m['optimizer.workflow.preview.description'](),
      status: m['optimizer.workflow.preview.status'](),
      icon: Eye,
    },
    {
      title: m['optimizer.workflow.restore.title'](),
      description: m['optimizer.workflow.restore.description'](),
      status: m['optimizer.workflow.restore.status'](),
      icon: History,
    },
  ];
  return (
    <OptimizerShell>
      <section className="op-hero op-width">
        <div className="op-hero-copy">
          <p className="op-eyebrow">
            <span />
            {m['optimizer.hero.eyebrow']()}
          </p>
          <h1>
            {m['optimizer.hero.title']()}
            <span>{m['optimizer.hero.accent']()}</span>
          </h1>
          <p className="op-lead">{m['optimizer.hero.description']()}</p>
          <div className="op-actions">
            <Link href="/download" className="op-button">
              <ArrowDownToLine size={18} />
              {m['optimizer.hero.download']()}
            </Link>
            <Link className="op-text-link" href="/features">
              {m['optimizer.hero.features']()}
              <ArrowRight size={17} />
            </Link>
          </div>
          <div className="op-hero-reassurance">
            <span>
              <Check size={14} />
              {m['optimizer.hero.free']()}
            </span>
            <span>
              <Check size={14} />
              {m['optimizer.hero.restore']()}
            </span>
          </div>
        </div>
        <WorkflowPreview
          name={envConfigs.app_name}
          steps={steps}
          settings={[
            m['optimizer.setting.game_mode'](),
            m['optimizer.setting.recording'](),
            m['optimizer.setting.power_plan'](),
          ]}
          labels={{
            preview: m['optimizer.preview.label'](),
            title: m['optimizer.preview.title'](),
            local: m['optimizer.preview.local'](),
            settings: m['optimizer.preview.settings'](),
            supported: m['optimizer.preview.supported'](),
            note: m['optimizer.preview.note'](),
            pending: m['optimizer.preview.pending'](),
            plan: m['optimizer.preview.plan'](),
            backup: m['optimizer.preview.backup'](),
          }}
        />
      </section>
      <div className="op-release-strip">
        <div className="op-width">
          <span className="op-release-label">
            <span className="op-dot" />
            {m['optimizer.release.label']()}
          </span>
          <p>
            {catalog.isError
              ? m['optimizer.release.unavailable']()
              : catalog.isPending
                ? m['optimizer.common.loading']()
                : catalog.data?.release
                  ? m['optimizer.release.available']({
                      version: catalog.data.release.version,
                    })
                  : m['optimizer.release.building']()}
          </p>
          <Link href="/download">
            {m['optimizer.release.details']()}
            <ArrowUpRight size={16} />
          </Link>
        </div>
      </div>
      <section className="op-section op-width" id="features">
        <div className="op-section-heading">
          <div>
            <p className="op-eyebrow">
              01 / {m['optimizer.home.features_eyebrow']()}
            </p>
            <h2>{m['optimizer.home.features_title']()}</h2>
          </div>
          <p>{m['optimizer.home.features_description']()}</p>
        </div>
        <FeatureList items={optimizerFeatures().slice(0, 3)} />
        <Link className="op-text-link op-section-link" href="/features">
          {m['optimizer.home.all_features']()}
          <ArrowRight size={16} />
        </Link>
      </section>
      <section className="op-process-section">
        <div className="op-width">
          <div className="op-section-heading">
            <div>
              <p className="op-eyebrow">
                02 / {m['optimizer.home.process_eyebrow']()}
              </p>
              <h2>{m['optimizer.home.process_title']()}</h2>
            </div>
            <p>{m['optimizer.home.process_description']()}</p>
          </div>
          <div className="op-process-grid">
            {steps.map((step, index) => (
              <article key={step.title}>
                <div>
                  <step.icon size={26} strokeWidth={1.5} />
                  <span>0{index + 1}</span>
                </div>
                <h3>{step.title}</h3>
                <p>{step.description}</p>
              </article>
            ))}
          </div>
        </div>
      </section>
      <section className="op-section op-width op-faq-section">
        <div>
          <p className="op-eyebrow">03 / {m['optimizer.home.faq_eyebrow']()}</p>
          <h2>{m['optimizer.home.faq_title']()}</h2>
          <Link href="/pricing" className="op-text-link">
            {m['optimizer.home.compare']()}
            <ArrowRight size={16} />
          </Link>
        </div>
        <FaqList items={optimizerFaqs()} />
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

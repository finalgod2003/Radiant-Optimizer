import { useState } from 'react';
import {
  ArrowDownToLine,
  ArrowRight,
  Check,
  Copy,
  MonitorDown,
  PackageOpen,
  ShieldCheck,
} from 'lucide-react';
import { toast } from 'sonner';

import { Link } from '@/core/i18n/navigation';
import { m } from '@/paraglide/messages.js';
import { getLocale } from '@/paraglide/runtime.js';
import {
  PageIntro,
  QueryNotice,
} from '@/components/optimizer/product-sections';

import { useOptimizerCatalog } from './catalog-query';
import { OptimizerShell } from './shell';

export function OptimizerDownload() {
  const catalog = useOptimizerCatalog();
  const release = catalog.data?.release;
  const [copied, setCopied] = useState(false);
  const steps = [
    {
      title: m['optimizer.download.step1.title'](),
      description: m['optimizer.download.step1.description'](),
    },
    {
      title: m['optimizer.download.step2.title'](),
      description: m['optimizer.download.step2.description'](),
    },
    {
      title: m['optimizer.download.step3.title'](),
      description: m['optimizer.download.step3.description'](),
    },
  ];
  async function copyHash() {
    if (!release) return;
    try {
      await navigator.clipboard.writeText(release.sha256);
      setCopied(true);
    } catch {
      toast.error(m['optimizer.download.copy_error']());
    }
  }
  return (
    <OptimizerShell>
      <PageIntro
        eyebrow={m['optimizer.download.eyebrow']()}
        title={m['optimizer.download.title']()}
        description={m['optimizer.download.description']()}
      />
      <section className="op-download-grid op-width">
        <div className="op-download-card">
          <div className="op-download-symbol">
            <MonitorDown size={44} strokeWidth={1.2} />
            <span className="op-badge">Windows x64</span>
          </div>
          {catalog.isPending ? (
            <QueryNotice text={m['optimizer.common.loading']()} />
          ) : catalog.isError ? (
            <QueryNotice
              error
              text={m['optimizer.download.load_error']()}
              action={m['optimizer.common.retry']()}
              onAction={() => void catalog.refetch()}
            />
          ) : release ? (
            <>
              <p className="op-eyebrow">{m['optimizer.download.ready']()}</p>
              <h2>
                {m['optimizer.download.version']({ version: release.version })}
              </h2>
              <p>{m['optimizer.download.package_description']()}</p>
              <a
                className="op-button"
                href={release.downloadUrl}
                rel="noopener noreferrer"
              >
                <ArrowDownToLine size={18} />
                {m['optimizer.download.button']()}
              </a>
              <dl className="op-download-meta">
                <div>
                  <dt>{m['optimizer.download.platform']()}</dt>
                  <dd>Windows x64</dd>
                </div>
                <div>
                  <dt>{m['optimizer.download.size']()}</dt>
                  <dd>
                    {new Intl.NumberFormat(getLocale(), {
                      maximumFractionDigits: 1,
                    }).format(release.sizeBytes / 1024 / 1024)}{' '}
                    MB
                  </dd>
                </div>
                <div className="op-hash-row">
                  <dt>SHA-256</dt>
                  <dd>
                    <code>{release.sha256}</code>
                    <button
                      type="button"
                      className="op-text-link"
                      onClick={() => void copyHash()}
                    >
                      {copied ? <Check size={14} /> : <Copy size={14} />}
                      {copied
                        ? m['optimizer.download.copied']()
                        : m['optimizer.download.copy']()}
                    </button>
                  </dd>
                </div>
              </dl>
            </>
          ) : (
            <>
              <p className="op-eyebrow">
                <PackageOpen size={15} />
                {m['optimizer.download.preparing']()}
              </p>
              <h2>{m['optimizer.download.no_release_title']()}</h2>
              <p>{m['optimizer.download.no_release_description']()}</p>
              <div className="op-notice">
                <p>{m['optimizer.download.no_release_note']()}</p>
              </div>
              <Link className="op-text-link" href="/features">
                {m['optimizer.download.view_scope']()}
                <ArrowRight size={16} />
              </Link>
            </>
          )}
        </div>
        <aside className="op-download-aside">
          <h2>{m['optimizer.download.requirements_title']()}</h2>
          <ul>
            <li>
              <Check size={16} />
              <span>{m['optimizer.download.requirement1']()}</span>
            </li>
            <li>
              <Check size={16} />
              <span>{m['optimizer.download.requirement2']()}</span>
            </li>
            <li>
              <Check size={16} />
              <span>{m['optimizer.download.requirement3']()}</span>
            </li>
            <li>
              <Check size={16} />
              <span>{m['optimizer.download.requirement4']()}</span>
            </li>
            <li>
              <Check size={16} />
              <span>{m['optimizer.download.requirement5']()}</span>
            </li>
          </ul>
          <div className="op-aside-note">
            <ShieldCheck size={22} />
            <h3>{m['optimizer.download.safety_title']()}</h3>
            <p>{m['optimizer.download.safety_description']()}</p>
            <Link className="op-text-link" href="/features#compatibility">
              {m['optimizer.download.support_link']()}
              <ArrowRight size={15} />
            </Link>
          </div>
        </aside>
      </section>
      <section className="op-section op-width">
        <div className="op-section-heading">
          <div>
            <p className="op-eyebrow">
              {m['optimizer.download.steps_eyebrow']()}
            </p>
            <h2>{m['optimizer.download.steps_title']()}</h2>
          </div>
          <Link className="op-text-link" href="/activate-device">
            {m['optimizer.nav.activate']()}
            <ArrowRight size={16} />
          </Link>
        </div>
        <div className="op-setup-grid">
          {steps.map((step, index) => (
            <article key={step.title}>
              <span className="op-index">0{index + 1}</span>
              <h3>{step.title}</h3>
              <p>{step.description}</p>
            </article>
          ))}
        </div>
      </section>
    </OptimizerShell>
  );
}

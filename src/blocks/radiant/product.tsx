import { useState } from 'react';
import { games } from '@/data/games';
import {
  ArrowDown,
  ArrowRight,
  ArrowUpRight,
  Check,
  ChevronDown,
  Download,
  Expand,
  Menu,
  Monitor,
  X,
} from 'lucide-react';

import { useSession } from '@/core/auth/client';
import { Link } from '@/core/i18n/navigation';
import { envConfigs } from '@/config';
import { SUPPORT_EMAIL } from '@/config/support';
import { m } from '@/paraglide/messages.js';
import { getLocale } from '@/paraglide/runtime.js';
import { useOptimizerCatalog } from '@/blocks/optimizer/catalog-query';
import { LocaleSelector } from '@/components/locale-selector';

import '@/styles/design-previews.css';

export type PreviewStyle = 'paper' | 'atlas' | 'cinema';

function PreviewBrand() {
  return (
    <a href="#preview-home" className="pv-brand">
      <img
        className="pv-brand-mark"
        src={envConfigs.app_logo || '/logo.svg'}
        width={36}
        height={36}
        alt=""
      />
      <span>{envConfigs.app_name}</span>
    </a>
  );
}

function PreviewHeader() {
  const [menuOpen, setMenuOpen] = useState(false);
  const { data: session } = useSession();
  return (
    <header className="pv-header pv-width">
      <PreviewBrand />
      <nav
        id="product-navigation"
        className={menuOpen ? 'is-open' : ''}
        aria-label={m['radiant.nav.menu']()}
        onClick={() => setMenuOpen(false)}
      >
        <Link href="/features">{m['optimizer.nav.features']()}</Link>
        <Link href="/pricing">{m['optimizer.nav.pricing']()}</Link>
        <Link href="/games">{m['optimizer.nav.games']()}</Link>
        <a href="#preview-faq">{m['radiant.product.navfaq']()}</a>
      </nav>
      <div className="pv-header-actions">
        <LocaleSelector
          variant="compact"
          label={m['common.navigation.language']()}
          className="pv-locale"
        />
        <Link
          className="pv-account-link"
          href={session?.user ? '/settings/licenses' : '/sign-in'}
        >
          {session?.user
            ? m['optimizer.nav.account']()
            : m['optimizer.nav.sign_in']()}
        </Link>
        <Link className="pv-header-download" href="/download">
          {m['optimizer.nav.download']()}
          <ArrowDown size={15} />
        </Link>
        <button
          className="pv-mobile-menu"
          aria-controls="product-navigation"
          aria-expanded={menuOpen}
          aria-label={
            menuOpen ? m['radiant.nav.close']() : m['radiant.nav.menu']()
          }
          onClick={() => setMenuOpen(!menuOpen)}
        >
          {menuOpen ? <X size={20} /> : <Menu size={20} />}
        </button>
      </div>
    </header>
  );
}

function ProductShot() {
  const [screen, setScreen] = useState<
    'overview' | 'optimize' | 'cpu' | 'recovery'
  >('overview');
  const [enlarged, setEnlarged] = useState(false);
  const label =
    screen === 'overview'
      ? m['radiant.product.overviewtab']()
      : screen === 'optimize'
        ? m['radiant.product.optimizetab']()
        : screen === 'cpu'
          ? m['radiant.product.cputab']()
          : m['radiant.product.recoverytab']();
  const src = `/design-preview/radiant-client-${screen}.png?v=0.5.0`;
  return (
    <figure className="pv-product-shot">
      <div className="pv-shot-toolbar">
        <div
          className="pv-shot-options"
          aria-label={m['radiant.product.platform']()}
        >
          <button
            aria-pressed={screen === 'overview'}
            onClick={() => setScreen('overview')}
          >
            {m['radiant.product.overviewtab']()}
          </button>
          <button
            aria-pressed={screen === 'optimize'}
            onClick={() => setScreen('optimize')}
          >
            {m['radiant.product.optimizetab']()}
          </button>
          <button
            aria-pressed={screen === 'cpu'}
            onClick={() => setScreen('cpu')}
          >
            {m['radiant.product.cputab']()}
          </button>
          <button
            aria-pressed={screen === 'recovery'}
            onClick={() => setScreen('recovery')}
          >
            {m['radiant.product.recoverytab']()}
          </button>
        </div>
        <button
          className="pv-expand"
          onClick={() => setEnlarged(true)}
          aria-label={m['radiant.product.enlarge']()}
        >
          <Expand size={16} />
        </button>
      </div>
      <button
        className="pv-shot-image"
        onClick={() => setEnlarged(true)}
        aria-label={m['radiant.product.enlarge']()}
      >
        <img
          src={src}
          alt={m['radiant.product.imagealt']({ screen: label })}
          width={1200}
          height={820}
          fetchPriority="high"
        />
      </button>
      <figcaption>
        <span>{m['radiant.product.reference']()}</span>
      </figcaption>
      {enlarged && (
        <dialog
          className="pv-image-dialog"
          aria-label={m['radiant.product.enlarge']()}
          ref={(element) => {
            if (element && !element.open) element.showModal();
          }}
          onCancel={() => setEnlarged(false)}
          onClick={(event) => {
            if (event.target === event.currentTarget) setEnlarged(false);
          }}
        >
          <div className="pv-image-dialog-head">
            <p>{m['radiant.product.reference']()}</p>
            <button
              autoFocus
              onClick={() => setEnlarged(false)}
              aria-label={m['radiant.product.close']()}
            >
              <X size={20} />
            </button>
          </div>
          <img
            src={src}
            alt={m['radiant.product.imagealt']({ screen: label })}
            width={1200}
            height={820}
          />
        </dialog>
      )}
    </figure>
  );
}

function ProductHero() {
  return (
    <section
      className="pv-product-hero pv-width"
      aria-labelledby="preview-title"
    >
      <div className="pv-hero-copy">
        <div className="pv-hero-heading">
          <p className="pv-eyebrow">{m['optimizer.hero.eyebrow']()}</p>
          <h1 id="preview-title">{envConfigs.app_name}</h1>
        </div>
        <div className="pv-hero-details">
          <p className="pv-tagline">{m['optimizer.hero.title']()}</p>
          <p className="pv-hero-description">
            {m['optimizer.hero.description']()}
          </p>
          <div className="pv-hero-actions">
            <Link className="pv-primary" href="/download">
              <Download size={17} />
              {m['optimizer.hero.download']()}
            </Link>
            <Link className="pv-text-link" href="/features">
              {m['optimizer.hero.features']()}
              <ArrowRight size={16} />
            </Link>
          </div>
          <p className="pv-platform">
            <Monitor size={14} />
            {m['radiant.product.platform']()}
            <span aria-hidden="true">/</span>
            {m['optimizer.hero.restore']()}
          </p>
        </div>
      </div>
      <ProductShot />
    </section>
  );
}

function ProductFeatures() {
  const features = [
    {
      title: m['optimizer.feature.settings.title'](),
      description: m['optimizer.feature.settings.description'](),
      items: [
        m['optimizer.feature.settings.detail'](),
        m['optimizer.pricing.free.settings'](),
      ],
    },
    {
      title: m['optimizer.feature.games.title'](),
      description: m['optimizer.feature.games.description'](),
      items: [
        m['optimizer.feature.games.detail'](),
        m['optimizer.feature.restore.title'](),
      ],
    },
    {
      title: m['optimizer.feature.monitoring.title'](),
      description: m['optimizer.feature.monitoring.description'](),
      items: [
        m['optimizer.feature.monitoring.detail'](),
        m['optimizer.pricing.premium.capture'](),
      ],
    },
  ];
  return (
    <section className="pv-features pv-width pv-section" id="preview-features">
      <div className="pv-section-heading">
        <div>
          <p className="pv-eyebrow">{m['optimizer.home.features_eyebrow']()}</p>
          <h2>{m['optimizer.home.features_title']()}</h2>
        </div>
        <p>{m['optimizer.home.features_description']()}</p>
      </div>
      <div className="pv-feature-list">
        {features.map((feature, index) => (
          <article key={feature.title} className="pv-feature">
            <span className="pv-feature-index" aria-hidden="true">
              0{index + 1}
            </span>
            <h3>{feature.title}</h3>
            <p>{feature.description}</p>
            <ul>
              {feature.items.map((item) => (
                <li key={item}>
                  <Check size={14} />
                  {item}
                </li>
              ))}
            </ul>
          </article>
        ))}
      </div>
    </section>
  );
}

function ProductPlans() {
  const catalog = useOptimizerCatalog();
  const price = catalog.data
    ? new Intl.NumberFormat(getLocale(), {
        style: 'currency',
        currency: catalog.data.currency.toUpperCase(),
      }).format(catalog.data.priceInCents / 100)
    : '—';
  const plans = [
    {
      id: 'free',
      name: m['optimizer.pricing.free.name'](),
      price: m['optimizer.pricing.free.price'](),
      description: m['optimizer.pricing.free.description'](),
      items: [
        m['optimizer.pricing.free.hardware'](),
        m['optimizer.pricing.free.preview'](),
        m['optimizer.pricing.free.restore'](),
      ],
      action: m['optimizer.hero.download'](),
      href: '/download',
    },
    {
      id: 'premium',
      name: m['optimizer.pricing.premium.name'](),
      price,
      description: m['optimizer.pricing.premium.description'](),
      items: [
        m['optimizer.pricing.premium.free'](),
        m['optimizer.pricing.premium.games'](),
        m['optimizer.pricing.premium.capture'](),
      ],
      action: m['optimizer.home.compare'](),
      href: '/pricing',
    },
  ] as const;
  return (
    <section className="pv-plans" id="preview-plans">
      <div className="pv-width pv-section">
        <div className="pv-section-heading">
          <div>
            <p className="pv-eyebrow">{m['optimizer.pricing.eyebrow']()}</p>
            <h2>{m['optimizer.pricing.title']()}</h2>
          </div>
          <p>{m['optimizer.pricing.description']()}</p>
        </div>
        <div className="pv-plan-grid">
          {plans.map((plan) => (
            <article key={plan.id} className={`pv-plan pv-plan-${plan.id}`}>
              <div className="pv-plan-intro">
                <div>
                  <h3>{plan.name}</h3>
                  <p>{plan.description}</p>
                </div>
                <span className="pv-plan-price">{plan.price}</span>
              </div>
              <ul>
                {plan.items.map((item) => (
                  <li key={item}>
                    <Check size={16} />
                    {item}
                  </li>
                ))}
              </ul>
              <Link
                className={plan.id === 'premium' ? 'pv-primary' : 'pv-outline'}
                href={plan.href}
              >
                {plan.action}
                <ArrowRight size={17} />
              </Link>
            </article>
          ))}
        </div>
        <p className="pv-plan-note">{m['optimizer.pricing.restore_note']()}</p>
      </div>
    </section>
  );
}

function ProductDownload() {
  return (
    <section className="pv-download pv-width" id="preview-download">
      <div>
        <p className="pv-eyebrow">{m['radiant.product.platform']()}</p>
        <h2>
          {m['radiant.product.downloadtitle']({ product: envConfigs.app_name })}
        </h2>
        <p>{m['optimizer.download.description']()}</p>
      </div>
      <div className="pv-download-action">
        <Link className="pv-primary" href="/download">
          <Download size={18} />
          {m['optimizer.hero.download']()}
        </Link>
        <p>{m['optimizer.download.package_description']()}</p>
      </div>
    </section>
  );
}

function ProductFaq() {
  const questions = [
    [m['optimizer.faq.browser.q'](), m['optimizer.faq.browser.a']()],
    [m['optimizer.faq.premium.q'](), m['optimizer.faq.premium.a']()],
    [m['optimizer.faq.games.q'](), m['optimizer.faq.games.a']()],
    [m['optimizer.faq.performance.q'](), m['optimizer.faq.performance.a']()],
  ];
  return (
    <section className="pv-faq pv-width pv-section" id="preview-faq">
      <h2>{m['radiant.product.faqtitle']()}</h2>
      <div>
        {questions.map(([question, answer]) => (
          <details key={question}>
            <summary>
              {question}
              <ChevronDown size={17} />
            </summary>
            <p>{answer}</p>
          </details>
        ))}
      </div>
    </section>
  );
}

function BrowserGames() {
  const featured = [games[0], games[2], games[4]];
  return (
    <section className="pv-games" id="preview-games">
      <div className="pv-width pv-section">
        <div className="pv-games-heading">
          <div>
            <p className="pv-eyebrow">{m['radiant.product.gameskicker']()}</p>
            <h2>{m['radiant.product.gamestitlehome']()}</h2>
          </div>
          <p>{m['radiant.product.gamesintro']()}</p>
        </div>
        <div className="pv-game-links">
          {featured.map((game) => (
            <Link key={game.id} href={`/games/${game.id}`}>
              <img
                src={game.image}
                alt=""
                width={168}
                height={112}
                loading="lazy"
              />
              <div>
                <h3>{game.title}</h3>
                <span>
                  {m['radiant.product.gameaction']()}
                  <ArrowUpRight size={14} />
                </span>
              </div>
            </Link>
          ))}
        </div>
        <Link href="/games" className="pv-text-link pv-browse-games">
          {m['radiant.product.browsegames']()} <ArrowRight size={16} />
        </Link>
      </div>
    </section>
  );
}

const directoryBadges = [
  {
    href: 'https://findly.tools/radiant-optimizer?utm_source=radiant-optimizer',
    src: 'https://findly.tools/badges/findly-tools-badge-light.svg',
    alt: 'Featured on Findly.tools',
    width: 175,
    height: 55,
  },
  {
    href: 'https://twelve.tools',
    src: 'https://twelve.tools/badge1-light.svg',
    alt: 'Featured on Twelve Tools',
    width: 148,
    height: 40,
  },
  {
    href: 'https://turbo0.com/item/radiant-optimizer',
    src: 'https://img.turbo0.com/badge-listed-light.svg',
    alt: 'Listed on Turbo0',
    height: 54,
  },
  {
    href: 'https://www.foundrlist.com/product/radiantoptimizer?utm_source=badge&utm_medium=embed',
    src: 'https://www.foundrlist.com/api/badge/radiantoptimizer',
    alt: 'Featured on FoundrList',
    width: 150,
    height: 48,
  },
  {
    href: 'https://fastlaunch.io',
    src: 'https://fastlaunch.io/images/badges/featured-light.svg',
    alt: 'Featured on FastLaunch',
    width: 221,
    height: 60,
  },
  {
    href: 'https://deeplaunch.io',
    src: 'https://deeplaunch.io/badge/badge_light.svg',
    alt: 'Featured on DeepLaunch.io',
    width: 200,
    height: 54,
  },
  {
    href: 'https://dofollow.tools',
    src: 'https://dofollow.tools/badge/badge_light.svg',
    alt: 'Featured on Dofollow.Tools',
    width: 200,
    height: 54,
  },
];

function PreviewFooter() {
  return (
    <footer className="pv-footer pv-width">
      <div>
        <PreviewBrand />
        <p>{m['optimizer.footer.description']()}</p>
      </div>
      <nav aria-label={m['optimizer.footer.description']()}>
        <Link href="/features">{m['optimizer.nav.features']()}</Link>
        <Link href="/pricing">{m['optimizer.nav.pricing']()}</Link>
        <Link href="/download">
          {m['optimizer.nav.download']()}
          <ArrowDown size={14} />
        </Link>
        <Link href="/settings/licenses">{m['optimizer.nav.account']()}</Link>
        <Link href="/activate-device">{m['optimizer.nav.activate']()}</Link>
        <Link href="/tools">{m['optimizer.nav.tools']()}</Link>
        <Link href="/privacy-policy">{m['optimizer.footer.privacy']()}</Link>
        <Link href="/terms-of-service">{m['optimizer.footer.terms']()}</Link>
        <a href={`mailto:${SUPPORT_EMAIL}`}>{SUPPORT_EMAIL}</a>
        <LocaleSelector
          variant="pill"
          label={m['common.navigation.language']()}
          className="pv-locale"
        />
      </nav>
      <ul className="pv-directory-badges">
        {directoryBadges.map((badge) => (
          <li key={badge.href}>
            <a href={badge.href} target="_blank" rel="noopener noreferrer">
              <img
                src={badge.src}
                alt={badge.alt}
                width={badge.width}
                height={badge.height}
                style={
                  badge.width
                    ? undefined
                    : { height: badge.height, width: 'auto' }
                }
                loading="lazy"
                decoding="async"
              />
            </a>
          </li>
        ))}
      </ul>
    </footer>
  );
}

export function ProductPage({ variant }: { variant: PreviewStyle }) {
  return (
    <div className={`preview-site pv-${variant}`} id="preview-home">
      <a className="pv-skip" href="#preview-title">
        {m['radiant.nav.skip']()}
      </a>
      <PreviewHeader />
      <main>
        <ProductHero />
        <ProductFeatures />
        <ProductPlans />
        <ProductDownload />
        <ProductFaq />
        <BrowserGames />
      </main>
      <PreviewFooter />
    </div>
  );
}

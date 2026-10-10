import { useState, type ReactNode } from 'react';
import { SOURCE_POST } from '@/data/games';
import { ArrowUpRight, Globe2, Menu, X } from 'lucide-react';

import { Link } from '@/core/i18n/navigation';
import { envConfigs } from '@/config';
import { m } from '@/paraglide/messages.js';
import { getLocale, setLocale } from '@/paraglide/runtime.js';
import { BuiltWithShipAny } from '@/components/built-with-shipany';

import '@/styles/radiant.css';

export function RadiantShell({
  children,
  home: _home = false,
}: {
  children: ReactNode;
  home?: boolean;
}) {
  const [menu, setMenu] = useState(false);
  const sections = [
    { id: 'library', href: '/games', label: m['radiant.nav.library']() },
    { id: 'tools', href: '/tools', label: m['radiant.nav.tools']() },
    { id: 'product', href: '/features', label: m['optimizer.nav.features']() },
  ];
  const locale = getLocale();
  return (
    <div className="radiant-site">
      <a className="skip-link" href="#main">
        {m['radiant.nav.skip']()}
      </a>
      <header className="radiant-header">
        <div className="site-width header-inner">
          <Link href="/" className="brand">
            <img src={envConfigs.app_logo} alt="" width={34} height={34} />
            <span>
              {envConfigs.app_name}
              <small>{m['radiant.nav.tagline']()}</small>
            </span>
          </Link>
          <nav
            aria-label={m['radiant.nav.menu']()}
            className={`main-nav ${menu ? 'is-open' : ''}`}
          >
            {sections.map((section) => (
              <Link
                key={section.id}
                href={section.href}
                onClick={() => setMenu(false)}
              >
                {section.label}
              </Link>
            ))}
          </nav>
          <div className="header-actions">
            <button
              className="locale-button"
              aria-label={locale === 'en' ? '切换到中文' : 'Switch to English'}
              onClick={() => setLocale(locale === 'en' ? 'zh' : 'en')}
            >
              <Globe2 size={16} />
              <span>{locale === 'en' ? 'EN' : '中文'}</span>
            </button>
            <a
              className="source-button"
              href={SOURCE_POST}
              target="_blank"
              rel="noopener noreferrer"
            >
              {m['radiant.nav.source']()}
              <ArrowUpRight size={15} />
            </a>
            <button
              className="mobile-menu"
              aria-expanded={menu}
              aria-label={
                menu ? m['radiant.nav.close']() : m['radiant.nav.menu']()
              }
              onClick={() => setMenu(!menu)}
            >
              {menu ? <X /> : <Menu />}
            </button>
          </div>
        </div>
      </header>
      <main id="main">{children}</main>
      <footer className="radiant-footer site-width">
        <div className="footer-top">
          <div>
            <Link href="/" className="brand">
              <img src={envConfigs.app_logo} alt="" width={30} height={30} />
              <span>{envConfigs.app_name}</span>
            </Link>
            <p>{m['radiant.footer.tagline']()}</p>
          </div>
          <div className="footer-links">
            <Link href="/games">{m['radiant.nav.library']()}</Link>
            <Link href="/tools">{m['radiant.nav.tools']()}</Link>
            <a href={SOURCE_POST} target="_blank" rel="noopener noreferrer">
              {m['radiant.footer.source']()}
              <ArrowUpRight size={14} />
            </a>
          </div>
        </div>
        <div className="footer-bottom">
          <p>
            © 2026 {envConfigs.app_name}. {m['radiant.footer.independent']()}
          </p>
          <BuiltWithShipAny className="shipany-credit" />
        </div>
      </footer>
    </div>
  );
}

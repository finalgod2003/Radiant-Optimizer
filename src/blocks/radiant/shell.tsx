import { useState, type ReactNode } from 'react';
import { ArrowDown, Menu, X } from 'lucide-react';

import { Link } from '@/core/i18n/navigation';
import { envConfigs } from '@/config';
import { m } from '@/paraglide/messages.js';

import '@/styles/radiant.css';

export function RadiantShell({
  children,
  home = false,
}: {
  children: ReactNode;
  home?: boolean;
}) {
  const [menu, setMenu] = useState(false);
  const sections = [
    { href: '/', label: m['radiant.product.productlink']() },
    {
      href: home ? '#library' : '/games#library',
      label: m['radiant.product.navgames'](),
    },
    { href: '/tools', label: m['radiant.nav.tools']() },
  ];
  const brand = (
    <>
      <span className="radiant-wordmark" aria-hidden="true">
        {envConfigs.app_name.slice(0, 1).toLowerCase()}.
      </span>
      <span>{envConfigs.app_name}</span>
    </>
  );
  return (
    <div className="radiant-site radiant-cinema">
      <a className="skip-link" href="#main">
        {m['radiant.nav.skip']()}
      </a>
      <header className="radiant-header">
        <div className="site-width header-inner">
          <Link href="/" className="brand">
            {brand}
          </Link>
          <nav
            id="radiant-navigation"
            aria-label={m['radiant.nav.menu']()}
            className={'main-nav ' + (menu ? 'is-open' : '')}
          >
            {sections.map((section) => (
              <Link
                key={section.href}
                href={section.href}
                onClick={() => setMenu(false)}
              >
                {section.label}
              </Link>
            ))}
          </nav>
          <div className="header-actions">
            <Link className="source-button" href="/download">
              {m['radiant.product.navdownload']()}
              <ArrowDown size={15} />
            </Link>
            <button
              className="mobile-menu"
              aria-expanded={menu}
              aria-controls="radiant-navigation"
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
              {brand}
            </Link>
            <p>{m['optimizer.footer.description']()}</p>
          </div>
          <div className="footer-links">
            <Link href="/">{m['radiant.product.productlink']()}</Link>
            <Link href="/games">{m['radiant.product.navgames']()}</Link>
            <Link href="/download">{m['radiant.product.primary']()}</Link>
          </div>
        </div>
        <div className="footer-bottom">
          <p>© 2026 {envConfigs.app_name}.</p>
        </div>
      </footer>
    </div>
  );
}

import { useState, type ReactNode } from 'react';
import { ArrowDownToLine, ArrowUpRight, Globe2, Menu, X } from 'lucide-react';

import { Link } from '@/core/i18n/navigation';

import '@/styles/optimizer.css';

type NavigationItem = { href: string; label: string };

export function ProductShell({
  children,
  name,
  logo,
  supportEmail,
  navigation,
  account,
  locale,
  onLocaleChange,
  labels,
}: {
  children: ReactNode;
  name: string;
  logo: string;
  supportEmail: string;
  navigation: NavigationItem[];
  account: NavigationItem;
  locale: string;
  onLocaleChange: () => void;
  labels: {
    skip: string;
    menu: string;
    close: string;
    switchLocale: string;
    download: string;
    tagline: string;
    footer: string;
    independent: string;
    privacy: string;
    terms: string;
    activate: string;
  };
}) {
  const [menuOpen, setMenuOpen] = useState(false);
  return (
    <div className="optimizer-site">
      <a className="op-skip" href="#main">
        {labels.skip}
      </a>
      <header className="op-header">
        <div className="op-width op-header-inner">
          <Link
            href="/"
            className="op-brand"
            onClick={() => setMenuOpen(false)}
          >
            <img src={logo || '/favicon.svg'} width={32} height={32} alt="" />
            <span>
              {name}
              <small>{labels.tagline}</small>
            </span>
          </Link>
          <nav
            id="product-navigation"
            className={`op-nav ${menuOpen ? 'is-open' : ''}`}
            aria-label={labels.menu}
          >
            {navigation.map((item) => (
              <Link
                key={item.href}
                href={item.href}
                onClick={() => setMenuOpen(false)}
              >
                {item.label}
              </Link>
            ))}
            <Link className="op-mobile-account" href={account.href}>
              {account.label}
            </Link>
          </nav>
          <div className="op-header-actions">
            <button
              className="op-locale"
              type="button"
              onClick={onLocaleChange}
              aria-label={labels.switchLocale}
            >
              <Globe2 size={15} />
              <span>{locale === 'en' ? 'EN' : '中文'}</span>
            </button>
            <Link className="op-account-link" href={account.href}>
              {account.label}
            </Link>
            <Link className="op-button op-button-small" href="/download">
              <ArrowDownToLine size={15} />
              {labels.download}
            </Link>
            <button
              className="op-menu-toggle"
              type="button"
              aria-label={menuOpen ? labels.close : labels.menu}
              aria-expanded={menuOpen}
              aria-controls="product-navigation"
              onClick={() => setMenuOpen(!menuOpen)}
            >
              {menuOpen ? <X size={22} /> : <Menu size={22} />}
            </button>
          </div>
        </div>
      </header>
      <main id="main">{children}</main>
      <footer className="op-footer op-width">
        <div className="op-footer-main">
          <div>
            <Link className="op-brand" href="/">
              <img src={logo || '/favicon.svg'} alt="" width={28} height={28} />
              <span>{name}</span>
            </Link>
            <p>{labels.footer}</p>
          </div>
          <div className="op-footer-links">
            <Link href="/activate-device">
              {labels.activate}
              <ArrowUpRight size={14} />
            </Link>
            <Link href="/privacy-policy">{labels.privacy}</Link>
            <Link href="/terms-of-service">{labels.terms}</Link>
            <a href={`mailto:${supportEmail}`}>{supportEmail}</a>
          </div>
        </div>
        <div className="op-footer-bottom">
          <span>© 2026 {name}</span>
          <p>{labels.independent}</p>
          <span className="op-platform">WINDOWS / x64</span>
        </div>
      </footer>
    </div>
  );
}

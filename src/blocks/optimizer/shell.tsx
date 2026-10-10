import type { ReactNode } from 'react';

import { useSession } from '@/core/auth/client';
import { envConfigs } from '@/config';
import { SUPPORT_EMAIL } from '@/config/support';
import { m } from '@/paraglide/messages.js';
import { LocaleSelector } from '@/components/locale-selector';
import { ProductShell } from '@/components/optimizer/product-shell';

export function OptimizerShell({ children }: { children: ReactNode }) {
  const { data: session } = useSession();
  return (
    <ProductShell
      name={envConfigs.app_name}
      logo={envConfigs.app_logo}
      supportEmail={SUPPORT_EMAIL}
      localeSelector={
        <LocaleSelector
          variant="compact"
          label={m['common.navigation.language']()}
          className="op-locale"
        />
      }
      navigation={[
        { href: '/features', label: m['optimizer.nav.features']() },
        { href: '/pricing', label: m['optimizer.nav.pricing']() },
        { href: '/tools', label: m['optimizer.nav.tools']() },
        { href: '/games', label: m['optimizer.nav.games']() },
      ]}
      account={{
        href: session?.user ? '/settings/licenses' : '/sign-in',
        label: session?.user
          ? m['optimizer.nav.account']()
          : m['optimizer.nav.sign_in'](),
      }}
      labels={{
        skip: m['optimizer.nav.skip'](),
        menu: m['optimizer.nav.menu'](),
        close: m['optimizer.nav.close'](),
        download: m['optimizer.nav.download'](),
        tagline: m['optimizer.nav.tagline'](),
        footer: m['optimizer.footer.description'](),
        independent: m['optimizer.footer.independent'](),
        privacy: m['optimizer.footer.privacy'](),
        terms: m['optimizer.footer.terms'](),
        activate: m['optimizer.nav.activate'](),
      }}
    >
      {children}
    </ProductShell>
  );
}

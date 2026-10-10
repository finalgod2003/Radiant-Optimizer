import { useMutation } from '@tanstack/react-query';
import {
  ArrowRight,
  Check,
  CreditCard,
  Loader2,
  ShieldCheck,
} from 'lucide-react';
import { toast } from 'sonner';

import { useSession } from '@/core/auth/client';
import { Link, useRouter } from '@/core/i18n/navigation';
import { apiPost } from '@/lib/api-client';
import { m } from '@/paraglide/messages.js';
import { getLocale } from '@/paraglide/runtime.js';
import {
  FaqList,
  PageIntro,
  QueryNotice,
} from '@/components/optimizer/product-sections';

import { useOptimizerCatalog } from './catalog-query';
import { optimizerErrorMessage } from './errors';
import { OptimizerShell } from './shell';

export function OptimizerPricing() {
  const catalog = useOptimizerCatalog();
  const router = useRouter();
  const { data: session, isPending: sessionPending } = useSession();
  const checkout = useMutation({
    mutationFn: () =>
      apiPost<{ checkout_url?: string }>('/api/payment/checkout', {
        product_id: catalog.data!.productId,
        redirect: '/settings/licenses',
      }),
    onSuccess: (data) => {
      try {
        const url = new URL(data.checkout_url ?? '');
        if (url.protocol !== 'https:') throw new Error();
        window.location.assign(url.href);
      } catch {
        toast.error(m['optimizer.pricing.checkout_error']());
      }
    },
    onError: (error: Error) => toast.error(optimizerErrorMessage(error)),
  });
  const price = catalog.data
    ? new Intl.NumberFormat(getLocale(), {
        style: 'currency',
        currency: catalog.data.currency.toUpperCase(),
      }).format(catalog.data.priceInCents / 100)
    : '—';
  const freeFeatures = [
    m['optimizer.pricing.free.hardware'](),
    m['optimizer.pricing.free.diagnostics'](),
    m['optimizer.pricing.free.settings'](),
    m['optimizer.pricing.free.preview'](),
    m['optimizer.pricing.free.restore'](),
  ];
  const premiumFeatures = [
    m['optimizer.pricing.premium.free'](),
    m['optimizer.pricing.premium.power'](),
    m['optimizer.pricing.premium.games'](),
    m['optimizer.pricing.premium.cpu'](),
    m['optimizer.pricing.premium.capture'](),
    m['optimizer.pricing.premium.device'](),
    m['optimizer.pricing.premium.offline'](),
  ];
  function buy() {
    if (!catalog.data?.checkoutEnabled || checkout.isPending || sessionPending)
      return;
    if (!session?.user) {
      router.push('/sign-in?callbackUrl=%2Fpricing');
      return;
    }
    checkout.mutate();
  }
  return (
    <OptimizerShell>
      <PageIntro
        eyebrow={m['optimizer.pricing.eyebrow']()}
        title={m['optimizer.pricing.title']()}
        description={m['optimizer.pricing.description']()}
      />
      <section className="op-pricing op-width">
        {catalog.isError && (
          <QueryNotice
            error
            text={m['optimizer.pricing.catalog_error']()}
            action={m['optimizer.common.retry']()}
            onAction={() => void catalog.refetch()}
          />
        )}
        <div className="op-plan-grid">
          <article className="op-plan">
            <div className="op-plan-top">
              <span className="op-eyebrow">
                {m['optimizer.pricing.free.name']()}
              </span>
              <span className="op-badge op-badge-muted">
                {m['optimizer.pricing.free.badge']()}
              </span>
            </div>
            <h2>{m['optimizer.pricing.free.price']()}</h2>
            <p className="op-plan-period">
              {m['optimizer.pricing.free.period']()}
            </p>
            <p className="op-plan-description">
              {m['optimizer.pricing.free.description']()}
            </p>
            <Link href="/download" className="op-button op-button-outline">
              {m['optimizer.pricing.free.cta']()}
              <ArrowRight size={16} />
            </Link>
            <ul>
              {freeFeatures.map((feature) => (
                <li key={feature}>
                  <Check size={16} />
                  <span>{feature}</span>
                </li>
              ))}
            </ul>
          </article>
          <article className="op-plan op-plan-premium">
            <div className="op-plan-top">
              <span className="op-eyebrow">
                {m['optimizer.pricing.premium.name']()}
              </span>
              <span className="op-badge">
                {m['optimizer.pricing.premium.badge']()}
              </span>
            </div>
            <h2 aria-live="polite">{price}</h2>
            <p className="op-plan-period">
              {m['optimizer.pricing.premium.period']()}
            </p>
            <p className="op-plan-description">
              {m['optimizer.pricing.premium.description']()}
            </p>
            <button
              className="op-button"
              type="button"
              disabled={
                !catalog.data?.checkoutEnabled ||
                checkout.isPending ||
                sessionPending
              }
              onClick={buy}
            >
              {checkout.isPending ? (
                <Loader2 size={16} className="op-spin" />
              ) : (
                <CreditCard size={16} />
              )}
              {checkout.isPending
                ? m['optimizer.pricing.opening']()
                : catalog.isPending
                  ? m['optimizer.common.loading']()
                  : !catalog.data?.checkoutEnabled
                    ? m['optimizer.pricing.unavailable']()
                    : session?.user
                      ? m['optimizer.pricing.buy']()
                      : m['optimizer.pricing.sign_in_buy']()}
            </button>
            <p className="op-plan-refund">
              {m['optimizer.pricing.refund_summary']()}{' '}
              <Link href="/terms-of-service">
                {m['optimizer.pricing.refund_terms']()}
              </Link>
            </p>
            <ul>
              {premiumFeatures.map((feature) => (
                <li key={feature}>
                  <Check size={16} />
                  <span>{feature}</span>
                </li>
              ))}
            </ul>
            <p className="op-plan-footnote">
              {m['optimizer.pricing.premium.scope']()}
            </p>
          </article>
        </div>
        {!catalog.isPending &&
          !catalog.isError &&
          !catalog.data?.checkoutEnabled && (
            <QueryNotice text={m['optimizer.pricing.not_configured']()} />
          )}
        <div className="op-pricing-note">
          <ShieldCheck size={21} />
          <p>{m['optimizer.pricing.restore_note']()}</p>
        </div>
      </section>
      <section className="op-section op-width op-faq-section">
        <div>
          <p className="op-eyebrow">{m['optimizer.pricing.faq_eyebrow']()}</p>
          <h2>{m['optimizer.pricing.faq_title']()}</h2>
        </div>
        <FaqList
          items={[
            {
              question: m['optimizer.pricing.faq.devices.q'](),
              answer: m['optimizer.pricing.faq.devices.a'](),
            },
            {
              question: m['optimizer.pricing.faq.offline.q'](),
              answer: m['optimizer.pricing.faq.offline.a'](),
            },
            {
              question: m['optimizer.pricing.faq.future.q'](),
              answer: m['optimizer.pricing.faq.future.a'](),
            },
          ]}
        />
      </section>
    </OptimizerShell>
  );
}

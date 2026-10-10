import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  ArrowRight,
  History,
  KeyRound,
  Loader2,
  Monitor,
  RefreshCw,
  Unplug,
} from 'lucide-react';
import { toast } from 'sonner';

import { useSession } from '@/core/auth/client';
import { Link } from '@/core/i18n/navigation';
import { envConfigs } from '@/config';
import { apiGet, apiPost } from '@/lib/api-client';
import type { LicenseSummary } from '@/lib/optimizer-contracts';
import { m } from '@/paraglide/messages.js';
import { getLocale } from '@/paraglide/runtime.js';
import { Badge } from '@/components/ui/badge';
import { Button, buttonVariants } from '@/components/ui/button';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';

import { optimizerErrorMessage } from './errors';

import '@/styles/optimizer.css';

export function OptimizerLicenses() {
  const { data: session } = useSession();
  const queryClient = useQueryClient();
  const [selected, setSelected] = useState<LicenseSummary | null>(null);
  const [refreshUntil] = useState(() => Date.now() + 120_000);
  const query = useQuery({
    queryKey: ['optimizer-licenses', session?.user.id],
    queryFn: () => apiGet<LicenseSummary[]>('/api/licenses'),
    enabled: !!session?.user,
    retry: 1,
    // Hosted checkout can return before its verified payment notification arrives.
    refetchInterval: () => (Date.now() < refreshUntil ? 5_000 : false),
    refetchIntervalInBackground: false,
  });
  const unbind = useMutation({
    mutationFn: (licenseId: string) =>
      apiPost<{ ok: true }>('/api/licenses', { action: 'unbind', licenseId }),
    onSuccess: () => {
      setSelected(null);
      toast.success(m['optimizer.licenses.unbound']());
      void queryClient.invalidateQueries({ queryKey: ['optimizer-licenses'] });
    },
    onError: (error: Error) => toast.error(optimizerErrorMessage(error)),
  });
  const statusLabels = {
    active: m['optimizer.licenses.status.active'](),
    revoked: m['optimizer.licenses.status.revoked'](),
    refunded: m['optimizer.licenses.status.refunded'](),
  };
  const date = (value: string) =>
    new Intl.DateTimeFormat(getLocale(), {
      dateStyle: 'medium',
      timeStyle: 'short',
    }).format(new Date(value));
  return (
    <div className="op-account-page">
      <div className="op-account-heading">
        <div>
          <h1>{m['optimizer.licenses.title']()}</h1>
          <p>{m['optimizer.licenses.description']()}</p>
        </div>
        <Link
          className={buttonVariants({ variant: 'outline' })}
          href="/download"
        >
          {m['optimizer.nav.download']()}
          <ArrowRight size={15} />
        </Link>
      </div>
      {query.isPending ? (
        <div className="op-account-loading" role="status">
          <Loader2 className="op-spin" size={18} />
          {m['optimizer.common.loading']()}
        </div>
      ) : query.isError ? (
        <div className="op-account-error" role="alert">
          <span>{m['optimizer.licenses.load_error']()}</span>
          <Button variant="outline" onClick={() => void query.refetch()}>
            <RefreshCw size={14} />
            {m['optimizer.common.retry']()}
          </Button>
        </div>
      ) : !query.data?.length ? (
        <div className="op-account-empty">
          <KeyRound size={36} strokeWidth={1.5} />
          <h2>{m['optimizer.licenses.empty_title']()}</h2>
          <p>{m['optimizer.licenses.empty_description']()}</p>
          <Button
            variant="outline"
            disabled={query.isFetching}
            onClick={() => void query.refetch()}
          >
            <RefreshCw
              size={14}
              className={query.isFetching ? 'op-spin' : ''}
            />
            {m['optimizer.common.refresh']()}
          </Button>
          <Link href="/pricing" className={buttonVariants()}>
            {m['optimizer.licenses.view_plans']()}
            <ArrowRight size={15} />
          </Link>
        </div>
      ) : (
        <>
          <div className="mb-5 flex justify-end">
            <Button
              variant="ghost"
              size="sm"
              disabled={query.isFetching}
              onClick={() => void query.refetch()}
            >
              <RefreshCw
                size={14}
                className={query.isFetching ? 'op-spin' : ''}
              />
              {m['optimizer.common.refresh']()}
            </Button>
          </div>
          <div className="op-license-grid">
            {query.data.map((license) => (
              <article className="op-license-card" key={license.id}>
                <div className="op-license-card-head">
                  <div>
                    <h2>
                      {m['optimizer.licenses.product']({
                        name: envConfigs.app_name,
                      })}
                    </h2>
                    <p className="op-license-id">{license.id}</p>
                  </div>
                  <Badge
                    variant={
                      license.status === 'active' ? 'secondary' : 'destructive'
                    }
                  >
                    {statusLabels[license.status]}
                  </Badge>
                </div>
                <dl className="op-license-details">
                  <div>
                    <dt>{m['optimizer.licenses.created']()}</dt>
                    <dd>{date(license.createdAt)}</dd>
                  </div>
                  <div>
                    <dt>{m['optimizer.licenses.device']()}</dt>
                    <dd className="op-license-device">
                      <Monitor size={16} />
                      {license.device?.name ??
                        m['optimizer.licenses.no_device']()}
                    </dd>
                  </div>
                  {license.device && (
                    <div>
                      <dt>{m['optimizer.licenses.last_seen']()}</dt>
                      <dd>{date(license.device.lastSeenAt)}</dd>
                    </div>
                  )}
                </dl>
                <div className="op-license-actions">
                  <p>
                    {license.status !== 'active'
                      ? m['optimizer.licenses.inactive_note']()
                      : license.device
                        ? m['optimizer.licenses.bound_note']()
                        : m['optimizer.licenses.available_note']()}
                  </p>
                  {license.status === 'active' &&
                    (license.device ? (
                      <Button
                        variant="outline"
                        size="sm"
                        onClick={() => setSelected(license)}
                        disabled={unbind.isPending}
                      >
                        <Unplug size={14} />
                        {m['optimizer.licenses.unbind']()}
                      </Button>
                    ) : (
                      <Link
                        href="/activate-device"
                        className={buttonVariants({
                          variant: 'outline',
                          size: 'sm',
                        })}
                      >
                        {m['optimizer.nav.activate']()}
                        <ArrowRight size={14} />
                      </Link>
                    ))}
                </div>
              </article>
            ))}
          </div>
        </>
      )}
      <div className="op-account-note">
        <History size={19} />
        <p>{m['optimizer.licenses.offline_note']()}</p>
      </div>
      <Dialog
        open={!!selected}
        onOpenChange={(open) => {
          if (!open && !unbind.isPending) setSelected(null);
        }}
      >
        <DialogContent>
          <DialogHeader>
            <DialogTitle>{m['optimizer.licenses.unbind_title']()}</DialogTitle>
            <DialogDescription>
              {m['optimizer.licenses.unbind_description']({
                device: selected?.device?.name ?? '',
              })}
            </DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <Button
              variant="outline"
              disabled={unbind.isPending}
              onClick={() => setSelected(null)}
            >
              {m['optimizer.common.cancel']()}
            </Button>
            <Button
              disabled={unbind.isPending || !selected}
              onClick={() => {
                if (selected) unbind.mutate(selected.id);
              }}
            >
              {unbind.isPending && <Loader2 size={14} className="op-spin" />}
              {m['optimizer.licenses.unbind_confirm']()}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}

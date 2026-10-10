import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Ban, Loader2, RefreshCw } from 'lucide-react';
import { toast } from 'sonner';

import { useSession } from '@/core/auth/client';
import { apiGet, apiPost, pageQuery, type PageResult } from '@/lib/api-client';
import type { LicenseSummary } from '@/lib/optimizer-contracts';
import { m } from '@/paraglide/messages.js';
import { getLocale } from '@/paraglide/runtime.js';
import { DataTable, type Column } from '@/components/data-table';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
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

type AdminLicense = LicenseSummary & { userId: string };
const PAGE_SIZE = 10;

export function OptimizerAdminLicenses() {
  const { data: session } = useSession();
  const userId = session?.user.id;
  const [page, setPage] = useState(1);
  const [selected, setSelected] = useState<AdminLicense | null>(null);
  const queryClient = useQueryClient();
  const query = useQuery({
    queryKey: ['optimizer-admin-licenses', userId, page],
    queryFn: () =>
      apiGet<PageResult<AdminLicense>>(
        pageQuery('/api/admin/licenses', { page, pageSize: PAGE_SIZE })
      ),
    placeholderData: (previousData, previousQuery) =>
      previousQuery?.queryKey[1] === userId ? previousData : undefined,
    enabled: !!userId,
    retry: 1,
  });
  const revoke = useMutation({
    mutationFn: (licenseId: string) =>
      apiPost<{ ok: true }>('/api/admin/licenses', {
        licenseId,
        action: 'revoke',
      }),
    onSuccess: () => {
      setSelected(null);
      toast.success(m['optimizer.admin.revoked']());
      void queryClient.invalidateQueries({
        queryKey: ['optimizer-admin-licenses'],
      });
      void queryClient.invalidateQueries({ queryKey: ['optimizer-licenses'] });
    },
    onError: (error: Error) => toast.error(optimizerErrorMessage(error)),
  });
  const statusLabels = {
    active: m['optimizer.licenses.status.active'](),
    revoked: m['optimizer.licenses.status.revoked'](),
    refunded: m['optimizer.licenses.status.refunded'](),
  };
  const columns: Column<AdminLicense>[] = [
    {
      header: m['optimizer.admin.license'](),
      cell: (license) => (
        <div>
          <p className="font-mono text-xs">{license.id}</p>
          <p className="text-muted-foreground mt-1 text-xs">
            {license.productId}
          </p>
        </div>
      ),
    },
    {
      header: m['optimizer.admin.user'](),
      cell: (license) => (
        <span className="font-mono text-xs">{license.userId}</span>
      ),
    },
    {
      header: m['optimizer.admin.status'](),
      cell: (license) => (
        <Badge
          variant={license.status === 'active' ? 'secondary' : 'destructive'}
        >
          {statusLabels[license.status]}
        </Badge>
      ),
    },
    {
      header: m['optimizer.licenses.device'](),
      cell: (license) => (
        <span className="text-xs">
          {license.device?.name ?? m['optimizer.licenses.no_device']()}
        </span>
      ),
    },
    {
      header: m['optimizer.licenses.created'](),
      cell: (license) => (
        <span className="text-muted-foreground text-xs">
          {new Intl.DateTimeFormat(getLocale(), { dateStyle: 'medium' }).format(
            new Date(license.createdAt)
          )}
        </span>
      ),
    },
    {
      header: m['optimizer.admin.actions'](),
      cell: (license) => (
        <Button
          variant="ghost"
          size="sm"
          disabled={license.status !== 'active' || revoke.isPending}
          onClick={() => setSelected(license)}
        >
          <Ban size={14} />
          {m['optimizer.admin.revoke']()}
        </Button>
      ),
    },
  ];
  return (
    <div className="op-account-page">
      <div className="op-account-heading">
        <div>
          <h1>{m['optimizer.admin.title']()}</h1>
          <p>{m['optimizer.admin.description']()}</p>
        </div>
      </div>
      {query.isError && (
        <div className="op-account-error" role="alert">
          <span>{m['optimizer.licenses.load_error']()}</span>
          <Button variant="outline" onClick={() => void query.refetch()}>
            <RefreshCw size={14} />
            {m['optimizer.common.retry']()}
          </Button>
        </div>
      )}
      <DataTable
        columns={columns}
        data={query.data?.items ?? []}
        total={query.data?.total ?? 0}
        page={page}
        pageSize={PAGE_SIZE}
        onPageChange={setPage}
        loading={query.isFetching}
        rowKey={(license) => license.id}
        onRefresh={() => query.refetch()}
        emptyText={
          query.isPending
            ? m['optimizer.common.loading']()
            : m['optimizer.admin.empty']()
        }
      />
      <Dialog
        open={!!selected}
        onOpenChange={(open) => {
          if (!open && !revoke.isPending) setSelected(null);
        }}
      >
        <DialogContent>
          <DialogHeader>
            <DialogTitle>{m['optimizer.admin.revoke_title']()}</DialogTitle>
            <DialogDescription>
              {m['optimizer.admin.revoke_description']()}
            </DialogDescription>
          </DialogHeader>
          <p className="font-mono text-xs break-all">{selected?.id}</p>
          <DialogFooter>
            <Button
              variant="outline"
              disabled={revoke.isPending}
              onClick={() => setSelected(null)}
            >
              {m['optimizer.common.cancel']()}
            </Button>
            <Button
              variant="destructive"
              disabled={revoke.isPending || !selected}
              onClick={() => {
                if (selected) revoke.mutate(selected.id);
              }}
            >
              {revoke.isPending && <Loader2 size={14} className="op-spin" />}
              {m['optimizer.admin.revoke']()}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}

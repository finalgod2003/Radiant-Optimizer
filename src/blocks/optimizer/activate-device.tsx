import { useState } from 'react';
import { useForm } from '@tanstack/react-form';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useSearch } from '@tanstack/react-router';
import {
  ArrowRight,
  CheckCircle2,
  KeyRound,
  Loader2,
  Monitor,
  ShieldCheck,
} from 'lucide-react';
import { z } from 'zod';

import { useSession } from '@/core/auth/client';
import { Link } from '@/core/i18n/navigation';
import { apiGet, apiPost } from '@/lib/api-client';
import type { LicenseSummary } from '@/lib/optimizer-contracts';
import { m } from '@/paraglide/messages.js';
import { getLocale } from '@/paraglide/runtime.js';
import { TextField } from '@/components/form-field';
import {
  PageIntro,
  QueryNotice,
} from '@/components/optimizer/product-sections';

import { optimizerErrorMessage } from './errors';
import { OptimizerShell } from './shell';

type DevicePreview = { deviceName: string; expiresAt: string };

export function OptimizerActivateDevice() {
  const search = useSearch({ strict: false }) as {
    userCode?: string;
    code?: string;
  };
  const initialCode = search.userCode || search.code || '';
  const { data: session, isPending: sessionPending } = useSession();
  const queryClient = useQueryClient();
  const [checkedCode, setCheckedCode] = useState('');
  const [approved, setApproved] = useState(false);
  const licenses = useQuery({
    queryKey: ['optimizer-licenses', session?.user.id],
    queryFn: () => apiGet<LicenseSummary[]>('/api/licenses'),
    enabled: !!session?.user,
    retry: 1,
  });
  const device = useQuery({
    queryKey: ['optimizer-device-preview', checkedCode, session?.user.id],
    queryFn: () =>
      apiGet<DevicePreview>(
        `/api/desktop/approve?userCode=${encodeURIComponent(checkedCode)}`
      ),
    enabled: !!checkedCode && !!session?.user && !approved,
    retry: false,
    staleTime: 0,
  });
  const available = (licenses.data ?? []).filter(
    (license) => license.status === 'active' && !license.device
  );
  const approval = useMutation({
    mutationFn: (licenseId: string) =>
      apiPost<{ ok: true }>('/api/desktop/approve', {
        userCode: checkedCode,
        licenseId,
      }),
    onSuccess: () => {
      setApproved(true);
      void queryClient.invalidateQueries({ queryKey: ['optimizer-licenses'] });
    },
  });
  const codeForm = useForm({
    defaultValues: { userCode: initialCode },
    validators: {
      onSubmit: z.object({
        userCode: z
          .string()
          .trim()
          .min(1, m['optimizer.activate.code_required']())
          .max(32, m['optimizer.activate.code_invalid']())
          .regex(/^[A-Za-z0-9\s-]+$/, m['optimizer.activate.code_invalid']()),
      }),
    },
    onSubmit: async ({ value }) => {
      const normalized = value.userCode.trim().toUpperCase();
      approval.reset();
      if (normalized === checkedCode) await device.refetch();
      else setCheckedCode(normalized);
    },
  });
  const approvalForm = useForm({
    defaultValues: { licenseId: '' },
    validators: {
      onSubmit: z.object({
        licenseId: z
          .string()
          .min(1, m['optimizer.activate.license_required']()),
      }),
    },
    onSubmit: async ({ value }) => {
      try {
        await approval.mutateAsync(value.licenseId);
      } catch {
        /* Mutation error is presented next to the action. */
      }
    },
  });
  const callback = `/activate-device${initialCode ? `?userCode=${encodeURIComponent(initialCode)}` : ''}`;
  return (
    <OptimizerShell>
      <PageIntro
        eyebrow={m['optimizer.activate.eyebrow']()}
        title={m['optimizer.activate.title']()}
        description={m['optimizer.activate.description']()}
      />
      <section className="op-activation op-width">
        {sessionPending ? (
          <QueryNotice text={m['optimizer.common.loading']()} />
        ) : !session?.user ? (
          <div className="op-activation-card">
            <KeyRound size={30} className="mb-5 text-[#c7f970]" />
            <h2>{m['optimizer.activate.sign_in_title']()}</h2>
            <p>{m['optimizer.activate.sign_in_description']()}</p>
            <Link
              href={`/sign-in?callbackUrl=${encodeURIComponent(callback)}`}
              className="op-button"
            >
              {m['optimizer.nav.sign_in']()}
              <ArrowRight size={16} />
            </Link>
          </div>
        ) : approved ? (
          <div
            className="op-activation-card op-activation-success"
            role="status"
          >
            <CheckCircle2 size={44} strokeWidth={1.4} />
            <h2>{m['optimizer.activate.success_title']()}</h2>
            <p>{m['optimizer.activate.success_description']()}</p>
            <div className="op-actions">
              <Link href="/settings/licenses" className="op-button">
                {m['optimizer.activate.manage']()}
                <ArrowRight size={16} />
              </Link>
            </div>
          </div>
        ) : (
          <div className="op-activation-card">
            <h2>{m['optimizer.activate.code_title']()}</h2>
            <p>{m['optimizer.activate.code_description']()}</p>
            <form
              onSubmit={(event) => {
                event.preventDefault();
                event.stopPropagation();
                void codeForm.handleSubmit();
              }}
            >
              <div className="op-activation-code">
                <codeForm.Field name="userCode">
                  {(field) => (
                    <TextField
                      field={field}
                      label={m['optimizer.activate.code_label']()}
                      placeholder={m['optimizer.activate.code_placeholder']()}
                      autoComplete="one-time-code"
                      required
                      disabled={approval.isPending}
                    />
                  )}
                </codeForm.Field>
              </div>
              <button
                type="submit"
                className="op-button op-button-outline"
                disabled={device.isFetching || approval.isPending}
              >
                {device.isFetching ? (
                  <Loader2 size={16} className="op-spin" />
                ) : (
                  <Monitor size={16} />
                )}
                {m['optimizer.activate.check_device']()}
              </button>
            </form>
            {device.isError && (
              <QueryNotice error text={optimizerErrorMessage(device.error)} />
            )}
            <codeForm.Subscribe selector={(state) => state.values.userCode}>
              {(enteredCode) =>
                device.data &&
                !device.isFetching &&
                !device.isError &&
                enteredCode.trim().toUpperCase() === checkedCode ? (
                  <div className="mt-8">
                    <div className="op-device-confirmation">
                      <Monitor size={25} />
                      <div>
                        <strong>{device.data.deviceName}</strong>
                        <p>
                          {m['optimizer.activate.expires']({
                            time: new Intl.DateTimeFormat(getLocale(), {
                              hour: '2-digit',
                              minute: '2-digit',
                            }).format(new Date(device.data.expiresAt)),
                          })}
                        </p>
                      </div>
                    </div>
                    {licenses.isPending ? (
                      <QueryNotice text={m['optimizer.common.loading']()} />
                    ) : licenses.isError ? (
                      <QueryNotice
                        error
                        text={m['optimizer.licenses.load_error']()}
                        action={m['optimizer.common.retry']()}
                        onAction={() => void licenses.refetch()}
                      />
                    ) : available.length === 0 ? (
                      <>
                        <QueryNotice
                          text={m['optimizer.activate.no_license']()}
                        />
                        <div className="op-actions">
                          <Link
                            href="/settings/licenses"
                            className="op-text-link"
                          >
                            {m['optimizer.activate.manage']()}
                            <ArrowRight size={15} />
                          </Link>
                          <Link href="/pricing" className="op-text-link">
                            {m['optimizer.licenses.view_plans']()}
                            <ArrowRight size={15} />
                          </Link>
                        </div>
                      </>
                    ) : (
                      <form
                        onSubmit={(event) => {
                          event.preventDefault();
                          event.stopPropagation();
                          void approvalForm.handleSubmit();
                        }}
                      >
                        <approvalForm.Field name="licenseId">
                          {(field) => (
                            <div className="op-license-select">
                              <label htmlFor="activation-license">
                                {m['optimizer.activate.license_label']()}
                              </label>
                              <select
                                id="activation-license"
                                name={field.name}
                                value={field.state.value}
                                onChange={(event) =>
                                  field.handleChange(event.target.value)
                                }
                                onBlur={field.handleBlur}
                                required
                                disabled={approval.isPending}
                                aria-invalid={
                                  field.state.meta.errors.length > 0
                                }
                              >
                                <option value="">
                                  {m[
                                    'optimizer.activate.license_placeholder'
                                  ]()}
                                </option>
                                {available.map((license) => (
                                  <option key={license.id} value={license.id}>
                                    {m['optimizer.activate.license_option']({
                                      id: license.id.slice(-8),
                                    })}
                                  </option>
                                ))}
                              </select>
                              {field.state.meta.errors.length > 0 && (
                                <p className="op-activation-error">
                                  {m['optimizer.activate.license_required']()}
                                </p>
                              )}
                            </div>
                          )}
                        </approvalForm.Field>
                        <p className="op-small-note">
                          {m['optimizer.activate.confirm_note']()}
                        </p>
                        {approval.isError && (
                          <p role="alert" className="op-activation-error">
                            {optimizerErrorMessage(approval.error)}
                          </p>
                        )}
                        <button
                          className="op-button"
                          type="submit"
                          disabled={approval.isPending}
                        >
                          {approval.isPending ? (
                            <Loader2 size={16} className="op-spin" />
                          ) : (
                            <ShieldCheck size={16} />
                          )}
                          {approval.isPending
                            ? m['optimizer.activate.approving']()
                            : m['optimizer.activate.approve']()}
                        </button>
                      </form>
                    )}
                  </div>
                ) : null
              }
            </codeForm.Subscribe>
          </div>
        )}
        <div className="op-notice">
          <ShieldCheck size={19} className="shrink-0" />
          <p>{m['optimizer.activate.security_note']()}</p>
        </div>
      </section>
    </OptimizerShell>
  );
}

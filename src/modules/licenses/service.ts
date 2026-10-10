import {
  and,
  count,
  desc,
  eq,
  gt,
  inArray,
  isNull,
  lte,
  or,
  sql,
} from 'drizzle-orm';

import { db } from '@/core/db';
import {
  desktopAuthGrant,
  deviceActivation,
  licenseEvent,
  order,
  productLicense,
  type DesktopAuthGrant,
  type DeviceActivation,
  type Order,
  type ProductLicense,
} from '@/config/db/schema';
import {
  DEVICE_GRANT_SECONDS,
  DEVICE_POLL_SECONDS,
  licenseSigningConfig,
  OPTIMIZER_PRODUCT_ID,
} from '@/config/optimizer';
import { getUuid } from '@/lib/hash';
import type {
  DesktopPollResponse,
  DesktopStartRequest,
  DesktopStartResponse,
  LicenseSummary,
} from '@/lib/optimizer-contracts';
import { hasOptimizerPaymentProof } from '@/lib/optimizer-payment-proof';

import { publicSigningKeys, randomToken, signLease, tokenHash } from './crypto';

export class LicenseError extends Error {
  constructor(
    message: string,
    readonly status = 400
  ) {
    super(message);
  }
}

/** Reject discounted/mismatched/unverified orders even if another caller marked them paid. */
export function isEligibleOptimizerOrder(paid: Order): boolean {
  if (paid.status !== 'paid' || paid.deletedAt) return false;
  try {
    return hasOptimizerPaymentProof(
      paid,
      JSON.parse(paid.paymentResult || '{}'),
      {
        paymentAmount: paid.paymentAmount ?? 0,
        paymentCurrency: paid.paymentCurrency || '',
        transactionId: paid.transactionId || undefined,
      },
      paid.paymentProvider
    );
  } catch {
    return false;
  }
}

/** Uses conditional writes, not interactive transactions, so the slot protocol also works on D1. */
export function createLicenseService(
  options: {
    database?: ReturnType<typeof db>;
    now?: () => Date;
    signing?: ReturnType<typeof licenseSigningConfig>;
  } = {}
) {
  const database = () => options.database ?? db();
  const now = options.now ?? (() => new Date());
  const signing = () => options.signing ?? licenseSigningConfig();

  async function changed(
    update: PromiseLike<unknown> & { returning?: () => PromiseLike<unknown[]> }
  ): Promise<boolean> {
    if (update.returning) return (await update.returning()).length > 0;
    const result = (await update) as {
      0?: { affectedRows?: number };
      affectedRows?: number;
      rowsAffected?: number;
      meta?: { changes?: number };
    };
    return (
      Number(
        result[0]?.affectedRows ??
          result.affectedRows ??
          result.rowsAffected ??
          result.meta?.changes ??
          0
      ) > 0
    );
  }

  function paidOrderIds() {
    return database()
      .select({ id: order.id })
      .from(order)
      .where(eq(order.status, 'paid'));
  }

  async function event(
    licenseId: string | null,
    type: string,
    actorUserId: string | null,
    detail: Record<string, unknown> = {},
    id = getUuid()
  ) {
    // Stable IDs make payment/revocation retry logging idempotent too.
    const existing = await database()
      .select({ id: licenseEvent.id })
      .from(licenseEvent)
      .where(eq(licenseEvent.id, id))
      .limit(1);
    if (existing.length) return;
    try {
      await database()
        .insert(licenseEvent)
        .values({
          id,
          licenseId,
          actorUserId,
          type,
          detail: JSON.stringify(detail),
          createdAt: now(),
        });
    } catch (error) {
      const rows = await database()
        .select({ id: licenseEvent.id })
        .from(licenseEvent)
        .where(eq(licenseEvent.id, id))
        .limit(1);
      if (!rows.length) throw error;
    }
  }

  async function license(id: string): Promise<ProductLicense | undefined> {
    const [row] = await database()
      .select()
      .from(productLicense)
      .where(eq(productLicense.id, id))
      .limit(1);
    return row;
  }

  async function fulfillOrder(orderId: string): Promise<ProductLicense | null> {
    const [paid]: Order[] = await database()
      .select()
      .from(order)
      .where(eq(order.id, orderId))
      .limit(1);
    if (!paid || !isEligibleOptimizerOrder(paid)) return null;
    let [existing]: ProductLicense[] = await database()
      .select()
      .from(productLicense)
      .where(eq(productLicense.orderId, paid.id))
      .limit(1);
    if (!existing) {
      const time = now();
      try {
        await database().insert(productLicense).values({
          id: getUuid(),
          orderId: paid.id,
          userId: paid.userId,
          productId: paid.productId!,
          status: 'active',
          createdAt: time,
          updatedAt: time,
        });
      } catch (error) {
        const duplicates = await database()
          .select()
          .from(productLicense)
          .where(eq(productLicense.orderId, paid.id))
          .limit(1);
        if (!duplicates.length) throw error;
      }
      [existing] = await database()
        .select()
        .from(productLicense)
        .where(eq(productLicense.orderId, paid.id))
        .limit(1);
    }
    if (!existing) throw new Error('License fulfillment did not persist');
    // A refund may race the insert. Re-read the authoritative order before returning.
    const [latest]: Order[] = await database()
      .select()
      .from(order)
      .where(eq(order.id, paid.id))
      .limit(1);
    if (latest?.status === 'refunded')
      await revoke(existing.id, 'refunded', null);
    await event(
      existing.id,
      'purchased',
      paid.userId,
      { orderNo: paid.orderNo, provider: paid.paymentProvider },
      `purchase:${existing.id}`
    );
    return (await license(existing.id))!;
  }

  async function reconcileUserOrders(userId: string) {
    const orders: Order[] = await database()
      .select()
      .from(order)
      .where(
        and(
          eq(order.userId, userId),
          eq(order.productId, OPTIMIZER_PRODUCT_ID),
          eq(order.status, 'paid'),
          isNull(order.deletedAt)
        )
      );
    for (const paid of orders) await fulfillOrder(paid.id);
    const refunds: Order[] = await database()
      .select()
      .from(order)
      .where(
        and(
          eq(order.userId, userId),
          eq(order.productId, OPTIMIZER_PRODUCT_ID),
          eq(order.status, 'refunded')
        )
      );
    for (const refund of refunds) await revokeOrder(refund.id);
  }

  async function summaries(rows: ProductLicense[]): Promise<LicenseSummary[]> {
    const ids = rows
      .map((row) => row.activeDeviceId)
      .filter((id): id is string => !!id);
    const devices: DeviceActivation[] = ids.length
      ? await database()
          .select()
          .from(deviceActivation)
          .where(inArray(deviceActivation.id, ids))
      : [];
    return rows.map((row) => {
      const device = devices.find(
        (d) => d.id === row.activeDeviceId && !d.revokedAt
      );
      const validSlot =
        !row.deviceSlotExpiresAt || row.deviceSlotExpiresAt > now();
      return {
        id: row.id,
        productId: row.productId,
        status: row.status as LicenseSummary['status'],
        createdAt: row.createdAt.toISOString(),
        device:
          device && validSlot && row.status === 'active'
            ? {
                id: device.id,
                name: device.name,
                lastSeenAt: device.lastSeenAt.toISOString(),
              }
            : null,
      };
    });
  }

  async function list(userId: string) {
    const rows: ProductLicense[] = await database()
      .select()
      .from(productLicense)
      .where(eq(productLicense.userId, userId))
      .orderBy(desc(productLicense.createdAt));
    return summaries(rows);
  }

  async function listAdmin(page: number, pageSize: number) {
    const rows: ProductLicense[] = await database()
      .select()
      .from(productLicense)
      .orderBy(desc(productLicense.createdAt))
      .limit(pageSize)
      .offset((page - 1) * pageSize);
    const [total] = await database()
      .select({ count: count() })
      .from(productLicense);
    const items = await summaries(rows);
    return {
      items: items.map((item, i) => ({ ...item, userId: rows[i].userId })),
      total: Number(total.count),
    };
  }

  async function unbind(userId: string, licenseId: string) {
    const row = await license(licenseId);
    if (!row || row.userId !== userId)
      throw new LicenseError('License not found', 404);
    if (!row.activeDeviceId) return;
    const deviceId = row.activeDeviceId;
    await database()
      .update(productLicense)
      .set({
        activeDeviceId: null,
        deviceSlotExpiresAt: null,
        updatedAt: now(),
      })
      .where(
        and(
          eq(productLicense.id, licenseId),
          eq(productLicense.userId, userId),
          eq(productLicense.activeDeviceId, deviceId)
        )
      );
    await database()
      .update(deviceActivation)
      .set({ revokedAt: now(), tokenHash: null })
      .where(
        and(
          eq(deviceActivation.id, deviceId),
          eq(deviceActivation.licenseId, licenseId)
        )
      );
    await event(licenseId, 'unbound', userId, { deviceId });
  }

  async function revoke(
    licenseId: string,
    status: 'revoked' | 'refunded',
    actorUserId: string | null
  ) {
    const row = await license(licenseId);
    if (!row) throw new LicenseError('License not found', 404);
    await database()
      .update(productLicense)
      .set({
        status,
        activeDeviceId: null,
        deviceSlotExpiresAt: null,
        updatedAt: now(),
      })
      .where(
        and(
          eq(productLicense.id, licenseId),
          status === 'revoked' ? eq(productLicense.status, 'active') : sql`1=1`
        )
      );
    await database()
      .update(deviceActivation)
      .set({ revokedAt: now(), tokenHash: null })
      .where(
        and(
          eq(deviceActivation.licenseId, licenseId),
          isNull(deviceActivation.revokedAt)
        )
      );
    await event(licenseId, status, actorUserId, {}, `${status}:${licenseId}`);
  }

  async function revokeOrder(orderId: string) {
    const rows: ProductLicense[] = await database()
      .select()
      .from(productLicense)
      .where(eq(productLicense.orderId, orderId));
    for (const row of rows) await revoke(row.id, 'refunded', null);
  }

  async function start(
    input: DesktopStartRequest,
    appUrl: string
  ): Promise<DesktopStartResponse> {
    const name = input.deviceName.trim();
    if (
      !name ||
      name.length > 80 ||
      /[\x00-\x1f]/.test(name) ||
      !/^[A-Za-z0-9_-]{16,128}$/.test(input.installationId)
    )
      throw new LicenseError('Invalid device details');
    await publicSigningKeys(signing());
    await cleanupExpiredGrants();
    const alphabet = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';
    const random = crypto.getRandomValues(new Uint8Array(10));
    const code = Array.from(
      random,
      (byte) => alphabet[byte % alphabet.length]
    ).join('');
    const userCode = code.slice(0, 5) + '-' + code.slice(5);
    const deviceCode = randomToken();
    const time = now();
    const expiresAt = new Date(time.getTime() + DEVICE_GRANT_SECONDS * 1000);
    await database()
      .insert(desktopAuthGrant)
      .values({
        id: getUuid(),
        deviceCodeHash: await tokenHash(deviceCode),
        userCodeHash: await tokenHash(code),
        deviceName: name,
        installationId: input.installationId,
        status: 'pending',
        createdAt: time,
        expiresAt,
      });
    const verificationUri = new URL('/activate-device', appUrl);
    verificationUri.searchParams.set('userCode', userCode);
    return {
      deviceCode,
      userCode,
      verificationUri: verificationUri.toString(),
      expiresAt: expiresAt.toISOString(),
      intervalSeconds: DEVICE_POLL_SECONDS,
    };
  }

  /** Keep a one-day troubleshooting window and remove at most 100 rows per start. */
  async function cleanupExpiredGrants() {
    const stale: { id: string }[] = await database()
      .select({ id: desktopAuthGrant.id })
      .from(desktopAuthGrant)
      .where(
        lte(desktopAuthGrant.expiresAt, new Date(now().getTime() - 86400000))
      )
      .orderBy(desktopAuthGrant.expiresAt)
      .limit(100);
    if (stale.length) {
      await database()
        .delete(desktopAuthGrant)
        .where(
          inArray(
            desktopAuthGrant.id,
            stale.map((row) => row.id)
          )
        );
    }
    return stale.length;
  }

  async function grantByCode(userCode: string): Promise<DesktopAuthGrant> {
    const code = userCode.replace(/[-\s]/g, '').toUpperCase();
    if (!/^[A-Z2-9]{10}$/.test(code))
      throw new LicenseError('Invalid or expired device code', 404);
    const [grant]: DesktopAuthGrant[] = await database()
      .select()
      .from(desktopAuthGrant)
      .where(eq(desktopAuthGrant.userCodeHash, await tokenHash(code)))
      .limit(1);
    if (!grant || grant.expiresAt <= now() || grant.status !== 'pending')
      throw new LicenseError('Invalid or expired device code', 404);
    return grant;
  }

  async function preview(userCode: string) {
    const grant = await grantByCode(userCode);
    return {
      deviceName: grant.deviceName,
      expiresAt: grant.expiresAt.toISOString(),
    };
  }

  async function approve(userId: string, userCode: string, licenseId: string) {
    const grant = await grantByCode(userCode);
    const owned = await license(licenseId);
    if (!owned || owned.userId !== userId)
      throw new LicenseError('License not found', 404);
    if (owned.status !== 'active')
      throw new LicenseError('License is not active', 409);
    // A single atomic compare-and-set reserves the license's only slot.
    const deviceId = getUuid();
    const time = now();
    const claimed = await changed(
      database()
        .update(productLicense)
        .set({
          activeDeviceId: deviceId,
          deviceSlotExpiresAt: grant.expiresAt,
          updatedAt: time,
        })
        .where(
          and(
            eq(productLicense.id, licenseId),
            eq(productLicense.userId, userId),
            eq(productLicense.status, 'active'),
            inArray(productLicense.orderId, paidOrderIds()),
            or(
              isNull(productLicense.activeDeviceId),
              lte(productLicense.deviceSlotExpiresAt, time)
            )
          )
        )
    );
    if (!claimed)
      throw new LicenseError(
        'License already has a device. Unbind it first.',
        409
      );
    try {
      await database().insert(deviceActivation).values({
        id: deviceId,
        licenseId,
        installationId: grant.installationId,
        name: grant.deviceName,
        createdAt: time,
        lastSeenAt: time,
      });
      const approved = await changed(
        database()
          .update(desktopAuthGrant)
          .set({ status: 'approved', licenseId, deviceId, approvedBy: userId })
          .where(
            and(
              eq(desktopAuthGrant.id, grant.id),
              eq(desktopAuthGrant.status, 'pending'),
              gt(desktopAuthGrant.expiresAt, now())
            )
          )
      );
      if (!approved)
        throw new LicenseError('Device code was already used', 409);
    } catch (error) {
      await database()
        .update(productLicense)
        .set({ activeDeviceId: null, deviceSlotExpiresAt: null })
        .where(
          and(
            eq(productLicense.id, licenseId),
            eq(productLicense.activeDeviceId, deviceId)
          )
        );
      await database()
        .update(deviceActivation)
        .set({ revokedAt: now() })
        .where(eq(deviceActivation.id, deviceId));
      throw error;
    }
    await event(licenseId, 'device_approved', userId, {
      deviceId,
      deviceName: grant.deviceName,
    });
  }

  async function poll(deviceCode: string): Promise<DesktopPollResponse> {
    if (!/^[A-Za-z0-9_-]{43}$/.test(deviceCode))
      throw new LicenseError('Invalid device code', 401);
    const [grant]: DesktopAuthGrant[] = await database()
      .select()
      .from(desktopAuthGrant)
      .where(eq(desktopAuthGrant.deviceCodeHash, await tokenHash(deviceCode)))
      .limit(1);
    if (!grant) throw new LicenseError('Invalid device code', 401);
    const time = now();
    if (grant.expiresAt <= time || grant.status === 'consumed')
      return { status: 'expired' };
    const polled = await changed(
      database()
        .update(desktopAuthGrant)
        .set({ lastPolledAt: time })
        .where(
          and(
            eq(desktopAuthGrant.id, grant.id),
            or(
              isNull(desktopAuthGrant.lastPolledAt),
              lte(
                desktopAuthGrant.lastPolledAt,
                new Date(time.getTime() - DEVICE_POLL_SECONDS * 1000)
              )
            )
          )
        )
    );
    if (!polled)
      throw new LicenseError('Poll too quickly. Retry after 5 seconds.', 429);
    if (grant.status === 'pending') return { status: 'pending' };
    const active = grant.licenseId ? await license(grant.licenseId) : undefined;
    if (
      !active ||
      active.status !== 'active' ||
      active.activeDeviceId !== grant.deviceId
    )
      return { status: 'expired' };
    const deviceToken = randomToken();
    const lease = await signLease(
      {
        licenseId: active.id,
        installationId: grant.installationId,
        deviceId: grant.deviceId!,
        productId: active.productId,
      },
      time,
      signing()
    );
    // Live-license predicate protects against a concurrent unbind/refund before exchange.
    const liveLicense = database()
      .select({ id: productLicense.id })
      .from(productLicense)
      .where(
        and(
          eq(productLicense.id, active.id),
          eq(productLicense.status, 'active'),
          inArray(productLicense.orderId, paidOrderIds()),
          eq(productLicense.activeDeviceId, grant.deviceId!)
        )
      );
    const exchanged = await changed(
      database()
        .update(desktopAuthGrant)
        .set({ status: 'consumed', consumedAt: time })
        .where(
          and(
            eq(desktopAuthGrant.id, grant.id),
            eq(desktopAuthGrant.status, 'approved'),
            gt(desktopAuthGrant.expiresAt, now()),
            inArray(desktopAuthGrant.licenseId, liveLicense)
          )
        )
    );
    if (!exchanged) return { status: 'expired' };
    await database()
      .update(deviceActivation)
      .set({ tokenHash: await tokenHash(deviceToken), lastSeenAt: time })
      .where(
        and(
          eq(deviceActivation.id, grant.deviceId!),
          isNull(deviceActivation.revokedAt)
        )
      );
    await database()
      .update(productLicense)
      .set({ deviceSlotExpiresAt: null, updatedAt: time })
      .where(
        and(
          eq(productLicense.id, active.id),
          eq(productLicense.activeDeviceId, grant.deviceId!),
          eq(productLicense.status, 'active')
        )
      );
    await event(active.id, 'device_activated', grant.approvedBy, {
      deviceId: grant.deviceId,
    });
    return { status: 'approved', deviceToken, lease };
  }

  async function refresh(deviceToken: string) {
    if (!/^[A-Za-z0-9_-]{43}$/.test(deviceToken))
      throw new LicenseError('Invalid device token', 401);
    const [device]: DeviceActivation[] = await database()
      .select()
      .from(deviceActivation)
      .where(
        and(
          eq(deviceActivation.tokenHash, await tokenHash(deviceToken)),
          isNull(deviceActivation.revokedAt)
        )
      )
      .limit(1);
    if (!device)
      throw new LicenseError('Device authorization was revoked', 401);
    const active = await license(device.licenseId);
    if (
      !active ||
      active.status !== 'active' ||
      active.activeDeviceId !== device.id ||
      active.deviceSlotExpiresAt
    )
      throw new LicenseError('License is not active for this device', 403);
    const [paid]: Order[] = await database()
      .select()
      .from(order)
      .where(eq(order.id, active.orderId))
      .limit(1);
    if (!paid || !isEligibleOptimizerOrder(paid))
      throw new LicenseError(
        'The purchase no longer authorizes this device',
        403
      );
    const lease = await signLease(
      {
        licenseId: active.id,
        installationId: device.installationId,
        deviceId: device.id,
        productId: active.productId,
      },
      now(),
      signing()
    );
    await database()
      .update(deviceActivation)
      .set({ lastSeenAt: now() })
      .where(
        and(
          eq(deviceActivation.id, device.id),
          isNull(deviceActivation.revokedAt)
        )
      );
    return { lease };
  }

  return {
    fulfillOrder,
    reconcileUserOrders,
    list,
    listAdmin,
    unbind,
    revoke,
    revokeOrder,
    start,
    preview,
    approve,
    poll,
    refresh,
    cleanupExpiredGrants,
    keys: () => publicSigningKeys(signing()),
  };
}

export const licenses = createLicenseService();

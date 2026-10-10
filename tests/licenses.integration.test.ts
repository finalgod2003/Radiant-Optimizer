import assert from 'node:assert/strict';
import {
  createPublicKey,
  generateKeyPairSync,
  randomUUID,
  verify,
} from 'node:crypto';
import { mkdtemp, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join, resolve, sep } from 'node:path';
import test from 'node:test';
import {
  generateSQLiteDrizzleJson,
  generateSQLiteMigration,
} from 'drizzle-kit/api';
import { eq } from 'drizzle-orm';
import Stripe from 'stripe';

import type { NewOrder, Order } from '../src/config/db/schema';
import type { DesktopPollResponse } from '../src/lib/optimizer-contracts';

test('desktop licensing on a real isolated SQLite database', async (t) => {
  const directory = await mkdtemp(join(tmpdir(), 'optimizer-licenses-'));
  process.env.DATABASE_PROVIDER = 'sqlite';
  process.env.DATABASE_URL = `file:${join(directory, 'test.db').replaceAll('\\', '/')}`;
  process.env.AUTH_SECRET = randomUUID() + randomUUID();
  const schema = await import('../src/config/db/schema');
  const { db } = await import('../src/core/db');
  const { createLicenseService, isEligibleOptimizerOrder } =
    await import('../src/modules/licenses/service');
  const {
    handleCheckoutSuccess,
    handlePaymentRefund,
    validateOptimizerPayment,
  } = await import('../src/modules/payment/service');
  const { StripeProvider } = await import('../src/core/payment/stripe');
  const { PaymentStatus } = await import('../src/core/payment/types');
  const {
    OPTIMIZER_PRODUCT_ID,
    optimizerCheckoutEnabled,
    validateOptimizerRelease,
  } = await import('../src/config/optimizer');
  const database = db();
  const before = await generateSQLiteDrizzleJson({});
  const after = await generateSQLiteDrizzleJson(schema);
  for (const statement of await generateSQLiteMigration(before, after))
    await database.$client.execute(statement);
  await database.$client.execute('PRAGMA foreign_keys = ON');
  const pair = generateKeyPairSync('rsa', {
    modulusLength: 2048,
    publicKeyEncoding: { type: 'spki', format: 'der' },
    privateKeyEncoding: { type: 'pkcs8', format: 'der' },
  });
  let timestamp = Date.parse('2026-10-10T08:00:00.000Z');
  const service = createLicenseService({
    database,
    now: () => new Date(timestamp),
    signing: {
      keyId: 'integration',
      privateKey: pair.privateKey.toString('base64url'),
    },
  });
  const buyer = 'test-buyer';
  const other = 'test-other';
  await database.insert(schema.user).values([
    { id: buyer, name: 'Test buyer', email: 'test-buyer@example.invalid' },
    { id: other, name: 'Other user', email: 'test-other@example.invalid' },
  ]);

  function checkoutResult(id: string) {
    return {
      object: 'checkout.session',
      id: `cs_${id}`,
      payment_intent: `pi_${id}`,
      mode: 'payment',
      status: 'complete',
      payment_status: 'paid',
      amount_subtotal: 5777,
      amount_total: 5777,
      currency: 'usd',
      livemode: false,
      total_details: { amount_discount: 0, amount_tax: 0 },
      metadata: {
        orderNo: `ORD_${id}`,
        productId: OPTIMIZER_PRODUCT_ID,
        licenseProduct: 'true',
      },
    };
  }

  async function purchase(status = 'paid') {
    const id = randomUUID();
    const result = checkoutResult(id);
    const values: NewOrder = {
      id,
      orderNo: `ORD_${id}`,
      userId: buyer,
      status,
      amount: 5777,
      currency: 'usd',
      productId: OPTIMIZER_PRODUCT_ID,
      paymentType: 'one-time',
      paymentProvider: 'stripe',
      paymentSessionId: result.id,
      transactionId: result.payment_intent,
      checkoutInfo: '{}',
      checkoutResult: JSON.stringify({ id: result.id, livemode: false }),
      paymentResult: JSON.stringify(result),
      paymentAmount: 5777,
      paymentCurrency: 'usd',
      paidAt: new Date(timestamp),
    };
    await database.insert(schema.order).values(values);
    const [row]: Order[] = await database
      .select()
      .from(schema.order)
      .where(eq(schema.order.id, id));
    return {
      row,
      result,
      session: {
        provider: 'stripe',
        paymentStatus: PaymentStatus.SUCCESS,
        paymentInfo: {
          paymentAmount: 5777,
          paymentCurrency: 'usd',
          transactionId: result.payment_intent,
        },
        paymentResult: result,
        metadata: result.metadata,
      },
    };
  }

  async function activated(licenseId: string, name = 'My PC') {
    const started = await service.start(
      { deviceName: name, installationId: randomUUID() },
      'http://localhost:3000'
    );
    await service.approve(buyer, started.userCode, licenseId);
    const polled = await service.poll(started.deviceCode);
    assert.equal(polled.status, 'approved');
    return polled as Extract<DesktopPollResponse, { status: 'approved' }>;
  }

  try {
    await t.test(
      'unpaid, wrong amount, wrong mode, currency, metadata and discounted orders never issue a license',
      async () => {
        const pending = await purchase('created');
        assert.equal(await service.fulfillOrder(pending.row.id), null);
        for (const paymentResult of [
          { ...pending.result, amount_total: 1 },
          { ...pending.result, livemode: true },
          { ...pending.result, currency: 'eur' },
          {
            ...pending.result,
            metadata: {
              ...pending.result.metadata,
              productId: 'different_product',
            },
          },
          {
            ...pending.result,
            total_details: { amount_discount: 100, amount_tax: 0 },
          },
        ]) {
          assert.throws(
            () =>
              validateOptimizerPayment(
                pending.row,
                { ...pending.session, paymentResult },
                'stripe'
              ),
            /authoritative order/
          );
          assert.equal(
            isEligibleOptimizerOrder({
              ...pending.row,
              status: 'paid',
              paymentResult: JSON.stringify(paymentResult),
            }),
            false
          );
        }
        assert.equal(
          isEligibleOptimizerOrder({
            ...pending.row,
            status: 'paid',
            amount: 1,
            paymentAmount: 1,
          }),
          false
        );
        await handleCheckoutSuccess(pending.session, 'stripe');
        assert.ok(await service.fulfillOrder(pending.row.id));
      }
    );

    await t.test(
      'concurrent retries create only one license and one purchase event',
      async () => {
        const paid = await purchase();
        const results = await Promise.all(
          Array.from({ length: 8 }, () => service.fulfillOrder(paid.row.id))
        );
        assert.equal(new Set(results.map((result) => result?.id)).size, 1);
        const licenses = await database
          .select()
          .from(schema.productLicense)
          .where(eq(schema.productLicense.orderId, paid.row.id));
        assert.equal(licenses.length, 1);
        const events = await database
          .select()
          .from(schema.licenseEvent)
          .where(eq(schema.licenseEvent.id, `purchase:${licenses[0].id}`));
        assert.equal(events.length, 1);
      }
    );

    await t.test(
      'other users cannot see, approve or unbind a purchased license',
      async () => {
        const paid = await purchase();
        const license = (await service.fulfillOrder(paid.row.id))!;
        const code = await service.start(
          { deviceName: 'Owner PC', installationId: randomUUID() },
          'http://localhost:3000'
        );
        assert.deepEqual(await service.list(other), []);
        await assert.rejects(
          service.approve(other, code.userCode, license.id),
          /not found/
        );
        await assert.rejects(service.unbind(other, license.id), /not found/);
        assert.equal(
          (await service.preview(code.userCode)).deviceName,
          'Owner PC'
        );
      }
    );

    await t.test(
      'two simultaneous devices cannot claim the same single-device license',
      async () => {
        const paid = await purchase();
        const license = (await service.fulfillOrder(paid.row.id))!;
        const starts = await Promise.all(
          ['PC A', 'PC B'].map((deviceName) =>
            service.start(
              { deviceName, installationId: randomUUID() },
              'http://localhost:3000'
            )
          )
        );
        const approvals = await Promise.allSettled(
          starts.map((started) =>
            service.approve(buyer, started.userCode, license.id)
          )
        );
        assert.equal(
          approvals.filter((result) => result.status === 'fulfilled').length,
          1
        );
        assert.equal(
          approvals.filter((result) => result.status === 'rejected').length,
          1
        );
        const responses = await Promise.all(
          starts.map((started) => service.poll(started.deviceCode))
        );
        assert.equal(
          responses.filter((result) => result.status === 'approved').length,
          1
        );
        assert.equal(
          responses.filter((result) => result.status === 'pending').length,
          1
        );
      }
    );

    await t.test(
      'lease verifies against SPKI, binds device and expires in exactly seven days',
      async () => {
        const paid = await purchase();
        const license = (await service.fulfillOrder(paid.row.id))!;
        const activation = await activated(license.id);
        const keys = await service.keys();
        const key = createPublicKey({
          key: Buffer.from(keys.keys[0].spki, 'base64url'),
          format: 'der',
          type: 'spki',
        });
        const payload = Buffer.from(activation.lease.payload, 'base64url');
        assert.equal(
          verify(
            'RSA-SHA256',
            payload,
            key,
            Buffer.from(activation.lease.signature, 'base64url')
          ),
          true
        );
        const parsed = JSON.parse(payload.toString('utf8'));
        assert.equal(parsed.licenseId, license.id);
        assert.equal(parsed.productId, OPTIMIZER_PRODUCT_ID);
        assert.equal(
          Date.parse(parsed.expiresAt) - Date.parse(parsed.issuedAt),
          7 * 86400000
        );
        assert.equal(
          verify(
            'RSA-SHA256',
            Buffer.from(payload.toString().replace(license.id, 'tampered')),
            key,
            Buffer.from(activation.lease.signature, 'base64url')
          ),
          false
        );
        const [stored] = await database
          .select()
          .from(schema.deviceActivation)
          .where(eq(schema.deviceActivation.id, parsed.deviceId));
        assert.notEqual(stored.tokenHash, activation.deviceToken);
        assert.equal(stored.installationId, parsed.installationId);
        timestamp += 86400000;
        const refreshed = await service.refresh(activation.deviceToken);
        assert.equal(
          JSON.parse(
            Buffer.from(refreshed.lease.payload, 'base64url').toString()
          ).issuedAt,
          new Date(timestamp).toISOString()
        );
      }
    );

    await t.test(
      'poll is rate-limited in SQLite and activation can only be exchanged once',
      async () => {
        const paid = await purchase();
        const license = (await service.fulfillOrder(paid.row.id))!;
        const start = await service.start(
          { deviceName: 'One exchange', installationId: randomUUID() },
          'http://localhost:3000'
        );
        assert.equal(start.deviceCode.length, 43);
        assert.equal(Date.parse(start.expiresAt) - timestamp, 300000);
        assert.equal(
          new URL(start.verificationUri).searchParams.get('userCode'),
          start.userCode
        );
        assert.deepEqual(await service.poll(start.deviceCode), {
          status: 'pending',
        });
        await assert.rejects(service.poll(start.deviceCode), /too quickly/);
        await service.approve(buyer, start.userCode, license.id);
        timestamp += 5000;
        const results = await Promise.allSettled([
          service.poll(start.deviceCode),
          service.poll(start.deviceCode),
        ]);
        const approved = results.filter(
          (result) =>
            result.status === 'fulfilled' && result.value.status === 'approved'
        );
        assert.equal(approved.length, 1);
        assert.deepEqual(await service.poll(start.deviceCode), {
          status: 'expired',
        });
        await assert.rejects(
          service.approve(buyer, start.userCode, license.id),
          /Invalid or expired/
        );
        await assert.rejects(
          service.refresh('this-is-a-web-session-token'),
          /Invalid device token/
        );
      }
    );

    await t.test(
      'expired requests reject approval and an abandoned slot is reusable',
      async () => {
        const paid = await purchase();
        const license = (await service.fulfillOrder(paid.row.id))!;
        const abandoned = await service.start(
          { deviceName: 'Abandoned PC', installationId: randomUUID() },
          'http://localhost:3000'
        );
        await service.approve(buyer, abandoned.userCode, license.id);
        const expired = await service.start(
          { deviceName: 'Expired code', installationId: randomUUID() },
          'http://localhost:3000'
        );
        timestamp += 300001;
        assert.deepEqual(await service.poll(abandoned.deviceCode), {
          status: 'expired',
        });
        await assert.rejects(
          service.approve(buyer, expired.userCode, license.id),
          /Invalid or expired/
        );
        assert.equal(
          (await service.list(buyer)).find((item) => item.id === license.id)
            ?.device,
          null
        );
        assert.equal(
          (await activated(license.id, 'Replacement PC')).status,
          'approved'
        );
      }
    );

    await t.test(
      'unbind immediately prevents refresh and permits another installation',
      async () => {
        const paid = await purchase();
        const license = (await service.fulfillOrder(paid.row.id))!;
        const previous = await activated(license.id, 'Previous PC');
        await service.unbind(buyer, license.id);
        await assert.rejects(service.refresh(previous.deviceToken), /revoked/);
        const next = await activated(license.id, 'Next PC');
        assert.notEqual(previous.deviceToken, next.deviceToken);
        assert.equal(
          (await service.list(buyer)).find((item) => item.id === license.id)
            ?.device?.name,
          'Next PC'
        );
      }
    );

    await t.test(
      'admin revocation persists through old payment success and reconciliation',
      async () => {
        const paid = await purchase();
        const license = (await service.fulfillOrder(paid.row.id))!;
        const previous = await activated(license.id);
        await service.revoke(license.id, 'revoked', other);
        await handleCheckoutSuccess(paid.session, 'stripe');
        await service.reconcileUserOrders(buyer);
        assert.equal(
          (await service.fulfillOrder(paid.row.id))?.status,
          'revoked'
        );
        await assert.rejects(service.refresh(previous.deviceToken), /revoked/);
      }
    );

    await t.test(
      'verified Stripe partial refund revokes and older success cannot reopen entitlement',
      async () => {
        const paid = await purchase();
        const license = (await service.fulfillOrder(paid.row.id))!;
        const previous = await activated(license.id);
        const secret = 'whsec_' + randomUUID();
        const stripe = new Stripe('sk_test_integration_placeholder');
        const payload = JSON.stringify({
          id: 'evt_' + randomUUID(),
          object: 'event',
          type: 'charge.refunded',
          data: {
            object: {
              id: 'ch_' + randomUUID(),
              object: 'charge',
              amount: 5777,
              amount_refunded: 100,
              currency: 'usd',
              livemode: false,
              payment_intent: paid.result.payment_intent,
              metadata: paid.result.metadata,
            },
          },
        });
        const provider = new StripeProvider({
          secretKey: 'sk_test_integration_placeholder',
          publishableKey: '',
          signingSecret: secret,
        });
        const signature = stripe.webhooks.generateTestHeaderString({
          payload,
          secret,
        });
        const event = await provider.getPaymentEvent({
          req: new Request('https://example.invalid/webhook', {
            method: 'POST',
            headers: { 'stripe-signature': signature },
            body: payload,
          }),
        });
        assert.equal(event.eventType, 'payment.refunded');
        const ids = await handlePaymentRefund(event.paymentSession!, 'stripe');
        assert.deepEqual(ids, [paid.row.id]);
        // Even before API orchestration catches up, refresh consults refunded order state.
        await assert.rejects(
          service.refresh(previous.deviceToken),
          /no longer authorizes/
        );
        for (const id of ids) await service.revokeOrder(id);
        await handlePaymentRefund(event.paymentSession!, 'stripe');
        await service.revokeOrder(paid.row.id);
        await handleCheckoutSuccess(paid.session, 'stripe');
        assert.equal(await service.fulfillOrder(paid.row.id), null);
        assert.equal(
          (await service.list(buyer)).find((item) => item.id === license.id)
            ?.status,
          'refunded'
        );
        await assert.rejects(service.refresh(previous.deviceToken), /revoked/);
        await assert.rejects(
          provider.getPaymentEvent({
            req: new Request('https://example.invalid/webhook', {
              method: 'POST',
              headers: { 'stripe-signature': signature },
              body: payload + ' ',
            }),
          })
        );
      }
    );

    await t.test(
      'refund arriving before checkout confirmation never issues a license',
      async () => {
        const pending = await purchase('created');
        await handlePaymentRefund(
          {
            provider: 'stripe',
            metadata: pending.result.metadata,
            paymentInfo: pending.session.paymentInfo,
            paymentResult: {
              object: 'charge',
              amount: 5777,
              amount_refunded: 5777,
              currency: 'usd',
              livemode: false,
            },
          },
          'stripe'
        );
        await handleCheckoutSuccess(pending.session, 'stripe');
        assert.equal(await service.fulfillOrder(pending.row.id), null);
        const [row] = await database
          .select()
          .from(schema.order)
          .where(eq(schema.order.id, pending.row.id));
        assert.equal(row.status, 'refunded');
      }
    );

    await t.test(
      'catalog requires merchant configuration and rejects unsafe release metadata',
      () => {
        assert.equal(optimizerCheckoutEnabled({}), false);
        assert.equal(
          validateOptimizerRelease({
            version: '1.0.0',
            downloadUrl: 'javascript:alert(1)',
            sha256: 'a'.repeat(64),
            sizeBytes: 32,
          }),
          null
        );
        assert.equal(
          validateOptimizerRelease({
            version: '1.0.0',
            downloadUrl: '/downloads/../private.key',
            sha256: 'a'.repeat(64),
            sizeBytes: 32,
          }),
          null
        );
        assert.ok(
          validateOptimizerRelease({
            version: '1.0.0',
            downloadUrl: '/downloads/optimizer.zip',
            sha256: 'a'.repeat(64),
            sizeBytes: 32,
          })
        );
      }
    );

    await t.test(
      'grant cleanup is bounded and preserves live device requests',
      async () => {
        const staleTime = new Date(timestamp - 2 * 86400000);
        const staleRows = Array.from({ length: 125 }, () => ({
          id: randomUUID(),
          deviceCodeHash: randomUUID(),
          userCodeHash: randomUUID(),
          deviceName: 'Expired request',
          installationId: randomUUID(),
          status: 'pending',
          createdAt: staleTime,
          expiresAt: staleTime,
        }));
        await database.insert(schema.desktopAuthGrant).values(staleRows);
        const live = await service.start(
          { deviceName: 'Live cleanup request', installationId: randomUUID() },
          'http://localhost:3000'
        );
        // start already performs one bounded pass; every explicit pass stays bounded too.
        const removed = await service.cleanupExpiredGrants();
        assert.ok(removed >= 0 && removed <= 100);
        assert.equal(
          (await service.preview(live.userCode)).deviceName,
          'Live cleanup request'
        );
        const remaining = await database
          .select()
          .from(schema.desktopAuthGrant)
          .where(eq(schema.desktopAuthGrant.deviceName, 'Expired request'));
        assert.equal(remaining.length, 0);
      }
    );
  } finally {
    database.$client.close();
    assert.ok(
      resolve(directory).startsWith(
        resolve(tmpdir()) + sep + 'optimizer-licenses-'
      )
    );
    await rm(directory, {
      recursive: true,
      force: true,
      maxRetries: 8,
      retryDelay: 100,
    }).catch((error: NodeJS.ErrnoException) => {
      if (error.code !== 'EBUSY') throw error;
      t.diagnostic(
        'Windows SQLite driver retains a handle until process exit; temporary test database remains in the OS temp directory.'
      );
    });
  }
});

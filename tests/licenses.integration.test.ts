import assert from 'node:assert/strict';
import {
  createPublicKey,
  generateKeyPairSync,
  randomUUID,
  sign,
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
    createCheckout,
    handleWebhook,
    handleCheckoutSuccess,
    handlePaymentRefund,
    validateOptimizerPayment,
  } = await import('../src/modules/payment/service');
  const { saveConfigs } = await import('../src/modules/config/service');
  const { StripeProvider } = await import('../src/core/payment/stripe');
  const { WaffoProvider } = await import('../src/core/payment/waffo');
  const { PaymentStatus } = await import('../src/core/payment/types');
  const {
    OPTIMIZER_PRODUCT_ID,
    OPTIMIZER_PRICE_IN_CENTS,
    optimizerCheckoutEnabled,
    validateOptimizerRelease,
  } = await import('../src/config/optimizer');
  const currentPrice = (OPTIMIZER_PRICE_IN_CENTS / 100).toFixed(2);
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

  function checkoutResult(id: string, amount = OPTIMIZER_PRICE_IN_CENTS) {
    return {
      object: 'checkout.session',
      id: `cs_${id}`,
      payment_intent: `pi_${id}`,
      mode: 'payment',
      status: 'complete',
      payment_status: 'paid',
      amount_subtotal: amount,
      amount_total: amount,
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

  async function purchase(status = 'paid', amount = OPTIMIZER_PRICE_IN_CENTS) {
    const id = randomUUID();
    const result = checkoutResult(id, amount);
    const values: NewOrder = {
      id,
      orderNo: `ORD_${id}`,
      userId: buyer,
      status,
      amount,
      currency: 'usd',
      productId: OPTIMIZER_PRODUCT_ID,
      paymentType: 'one-time',
      paymentProvider: 'stripe',
      paymentSessionId: result.id,
      transactionId: result.payment_intent,
      checkoutInfo: '{}',
      checkoutResult: JSON.stringify({ id: result.id, livemode: false }),
      paymentResult: JSON.stringify(result),
      paymentAmount: amount,
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
          paymentAmount: amount,
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

  const waffoKey = generateKeyPairSync('rsa', {
    modulusLength: 2048,
    publicKeyEncoding: { type: 'spki', format: 'pem' },
    privateKeyEncoding: { type: 'pkcs8', format: 'pem' },
  });
  const waffo = new WaffoProvider({
    merchantId: 'MER_integration',
    storeId: 'STO_integration',
    productId: 'PROD_integration',
    privateKey: waffoKey.privateKey,
    webhookPublicKey: waffoKey.publicKey,
    environment: 'test',
  });

  async function waffoPurchase(amount = OPTIMIZER_PRICE_IN_CENTS) {
    const id = randomUUID();
    const orderNo = `ORD_${id}`;
    const event = {
      id: `PAY_${id.replaceAll('-', '')}`,
      eventId: `PAY_${id.replaceAll('-', '')}`,
      timestamp: new Date().toISOString(),
      eventType: 'order.completed',
      storeId: 'STO_integration',
      mode: 'test',
      data: {
        orderId: `ORD_${id.replaceAll('-', '')}`,
        orderStatus: 'completed',
        paymentId: `PAY_${id.replaceAll('-', '')}`,
        paymentStatus: 'succeeded',
        paymentDate: new Date().toISOString().slice(0, 10),
        buyerEmail: 'test-buyer@example.invalid',
        currency: 'USD',
        chargedAmount: (amount / 100).toFixed(2),
        listPrice: {
          total: (amount / 100).toFixed(2),
          subtotal: (amount / 100).toFixed(2),
          taxAmount: '0.00',
        },
        orderMerchantExternalId: orderNo,
        orderMetadata: {
          orderNo,
          userId: buyer,
          productId: OPTIMIZER_PRODUCT_ID,
          waffoProductId: 'PROD_integration',
          licenseProduct: 'true',
        },
      },
    };
    await database.insert(schema.order).values({
      id,
      orderNo,
      userId: buyer,
      status: 'created',
      amount,
      currency: 'usd',
      productId: OPTIMIZER_PRODUCT_ID,
      paymentType: 'one-time',
      paymentProvider: 'waffo',
      paymentProductId: 'PROD_integration',
      paymentSessionId: orderNo,
      checkoutInfo: '{}',
      checkoutResult: JSON.stringify({
        sessionId: `cs_${id}`,
        orderNo,
        mode: 'test',
        storeId: 'STO_integration',
        productId: 'PROD_integration',
      }),
    });
    const [row]: Order[] = await database
      .select()
      .from(schema.order)
      .where(eq(schema.order.id, id));
    return { row, event };
  }

  function waffoRequest(event: unknown) {
    const body = JSON.stringify(event);
    const t = Date.now().toString();
    const signature = sign(
      'RSA-SHA256',
      Buffer.from(`${t}.${body}`),
      waffoKey.privateKey
    ).toString('base64');
    return new Request('https://example.invalid/api/payment/notify/waffo', {
      method: 'POST',
      body,
      headers: { 'X-Waffo-Signature': `t=${t},v1=${signature}` },
    });
  }

  async function verifiedWaffoEvent(event: unknown) {
    return waffo.getPaymentEvent({ req: waffoRequest(event) });
  }

  function waffoRefund(
    event: Awaited<ReturnType<typeof waffoPurchase>>['event']
  ) {
    return {
      ...event,
      id: 'REF_integration',
      eventId: 'REF_integration',
      eventType: 'refund.succeeded',
      data: {
        ...event.data,
        refundId: 'REF_integration',
        refundStatus: 'succeeded',
        refundedAmount: '1.00',
        originalChargedAmount: event.data.chargedAmount,
        originalPayment: event.data.listPrice,
      },
    };
  }

  try {
    await t.test(
      'Waffo checkout and webhook use the shared payment manager and persist correlated orders',
      async () => {
        await saveConfigs({
          app_url: 'https://shop.example.invalid',
          default_payment_provider: 'waffo',
          waffo_enabled: 'true',
          waffo_environment: 'test',
          waffo_merchant_id: 'MER_integration',
          waffo_store_id: 'STO_integration',
          waffo_product_id: 'PROD_integration',
          waffo_private_key: waffoKey.privateKey,
          waffo_webhook_public_key: waffoKey.publicKey,
        });
        const originalFetch = globalThis.fetch;
        let payload: Record<string, unknown> = {};
        try {
          globalThis.fetch = async (_input, init) => {
            payload = JSON.parse(String(init?.body)) as Record<string, unknown>;
            return Response.json({
              data: {
                sessionId: `cs_${randomUUID()}`,
                checkoutUrl:
                  'https://pancake.waffo.ai/store/test/checkout/session',
              },
            });
          };
          for (const amount of [1, 5777]) {
            await assert.rejects(
              createCheckout({
                userId: buyer,
                provider: 'waffo',
                paymentOrder: {
                  productId: OPTIMIZER_PRODUCT_ID,
                  type: (await import('../src/core/payment/types')).PaymentType
                    .ONE_TIME,
                  price: { amount, currency: 'usd' },
                },
              }),
              /Invalid optimizer checkout configuration/
            );
          }
          const checkout = await createCheckout({
            userId: buyer,
            userEmail: 'test-buyer@example.invalid',
            provider: 'waffo',
            credits: 0,
            paymentOrder: {
              productId: OPTIMIZER_PRODUCT_ID,
              type: (await import('../src/core/payment/types')).PaymentType
                .ONE_TIME,
              price: { amount: OPTIMIZER_PRICE_IN_CENTS, currency: 'usd' },
              successUrl: 'https://shop.example.invalid/settings/licenses',
              metadata: {
                userId: 'untrusted-value',
                orderNo: 'untrusted-value',
                productId: 'untrusted-value',
              },
            },
          });
          const [persisted]: Order[] = await database
            .select()
            .from(schema.order)
            .where(eq(schema.order.orderNo, checkout.checkoutInfo.sessionId));
          assert.equal(persisted.paymentProvider, 'waffo');
          assert.equal(persisted.paymentProductId, 'PROD_integration');
          assert.equal(persisted.amount, OPTIMIZER_PRICE_IN_CENTS);
          assert.deepEqual(payload.priceSnapshot, {
            amount: currentPrice,
            taxIncluded: true,
            taxCategory: 'digital_goods',
          });
          assert.equal(payload.orderMerchantExternalId, persisted.orderNo);
          const template = await waffoPurchase();
          const event = {
            ...template.event,
            data: {
              ...template.event.data,
              orderMerchantExternalId: persisted.orderNo,
              orderMetadata: payload.metadata,
            },
          };
          const processed = await handleWebhook({
            req: waffoRequest(event),
            provider: 'waffo',
          });
          assert.deepEqual(processed.orderIds, [persisted.id]);
          const license = await service.fulfillOrder(persisted.id);
          assert.ok(license);
          assert.deepEqual(
            (
              await handleWebhook({
                req: waffoRequest(event),
                provider: 'waffo',
              })
            ).orderIds,
            [persisted.id]
          );
          assert.equal(
            (await service.fulfillOrder(persisted.id))?.id,
            license.id
          );

          const rotated = generateKeyPairSync('rsa', {
            modulusLength: 2048,
            publicKeyEncoding: { type: 'spki', format: 'pem' },
            privateKeyEncoding: { type: 'pkcs8', format: 'pem' },
          });
          await saveConfigs({ waffo_webhook_public_key: rotated.publicKey });
          await assert.rejects(
            handleWebhook({ req: waffoRequest(event), provider: 'waffo' }),
            /signature/
          );
        } finally {
          globalThis.fetch = originalFetch;
          await saveConfigs({
            waffo_enabled: 'false',
            default_payment_provider: 'stripe',
          });
        }
      }
    );

    await t.test(
      'invalid Waffo settings do not interrupt verified Stripe events',
      async () => {
        const secret = 'whsec_' + randomUUID();
        await saveConfigs({
          waffo_enabled: 'true',
          waffo_private_key: 'invalid-private-key',
          stripe_secret_key: 'sk_test_integration_placeholder',
          stripe_signing_secret: secret,
        });
        const stripe = new Stripe('sk_test_integration_placeholder');
        const pending = await purchase('created');
        const body = JSON.stringify({
          id: 'evt_' + randomUUID(),
          object: 'event',
          type: 'checkout.session.completed',
          data: { object: pending.result },
        });
        const signature = stripe.webhooks.generateTestHeaderString({
          payload: body,
          secret,
        });
        try {
          const event = await handleWebhook({
            provider: 'stripe',
            req: new Request('https://example.invalid/notify/stripe', {
              method: 'POST',
              body,
              headers: { 'stripe-signature': signature },
            }),
          });
          assert.deepEqual(event.orderIds, [pending.row.id]);
          assert.ok(await service.fulfillOrder(pending.row.id));
        } finally {
          await saveConfigs({ waffo_enabled: 'false' });
        }
      }
    );

    await t.test(
      'Waffo actual charges remain authoritative without optional price breakdowns',
      async () => {
        const pending = await waffoPurchase();
        const event = {
          ...pending.event,
          data: { ...pending.event.data, listPrice: undefined },
        };
        await handleCheckoutSuccess(
          (await verifiedWaffoEvent(event)).paymentSession!,
          'waffo'
        );
        assert.ok(await service.fulfillOrder(pending.row.id));
        const refund = waffoRefund(pending.event);
        const withoutBreakdown = {
          ...refund,
          data: { ...refund.data, originalPayment: undefined },
        };
        assert.deepEqual(
          await handlePaymentRefund(
            (await verifiedWaffoEvent(withoutBreakdown)).paymentSession!,
            'waffo'
          ),
          [pending.row.id]
        );
      }
    );

    await t.test(
      'verified Waffo purchase issues one license and a partial refund revokes it',
      async () => {
        const pending = await waffoPurchase();
        const completed = (await verifiedWaffoEvent(pending.event))
          .paymentSession!;
        assert.equal(await service.fulfillOrder(pending.row.id), null);
        await handleCheckoutSuccess(completed, 'waffo');
        const license = (await service.fulfillOrder(pending.row.id))!;
        assert.ok(license);
        await handleCheckoutSuccess(completed, 'waffo');
        assert.equal(
          (await service.fulfillOrder(pending.row.id))!.id,
          license.id
        );
        const active = await activated(license.id);
        const refund = (await verifiedWaffoEvent(waffoRefund(pending.event)))
          .paymentSession!;
        for (const id of await handlePaymentRefund(refund, 'waffo'))
          await service.revokeOrder(id);
        await handlePaymentRefund(refund, 'waffo');
        await handleCheckoutSuccess(completed, 'waffo');
        assert.equal(await service.fulfillOrder(pending.row.id), null);
        assert.equal(
          (await service.list(buyer)).find((item) => item.id === license.id)
            ?.status,
          'refunded'
        );
        await assert.rejects(service.refresh(active.deviceToken), /revoked/);
      }
    );

    await t.test(
      'Waffo refund before payment confirmation prevents entitlement creation',
      async () => {
        const pending = await waffoPurchase();
        const refund = (await verifiedWaffoEvent(waffoRefund(pending.event)))
          .paymentSession!;
        assert.deepEqual(await handlePaymentRefund(refund, 'waffo'), [
          pending.row.id,
        ]);
        await handleCheckoutSuccess(
          (await verifiedWaffoEvent(pending.event)).paymentSession!,
          'waffo'
        );
        assert.equal(await service.fulfillOrder(pending.row.id), null);
      }
    );

    await t.test(
      'Waffo signed but mismatched payment proof cannot authorize a license',
      async () => {
        const pending = await waffoPurchase();
        const valid = (await verifiedWaffoEvent(pending.event)).paymentSession!;
        for (const event of [
          { ...pending.event, mode: 'prod' },
          { ...pending.event, storeId: 'STO_another' },
          {
            ...pending.event,
            data: { ...pending.event.data, chargedAmount: '0.01' },
          },
          {
            ...pending.event,
            data: { ...pending.event.data, chargedAmount: undefined },
          },
          {
            ...pending.event,
            data: { ...pending.event.data, orderMerchantExternalId: 'another' },
          },
          {
            ...pending.event,
            data: {
              ...pending.event.data,
              orderMetadata: {
                ...pending.event.data.orderMetadata,
                userId: other,
              },
            },
          },
          {
            ...pending.event,
            data: {
              ...pending.event.data,
              orderMetadata: {
                ...pending.event.data.orderMetadata,
                waffoProductId: 'PROD_other',
              },
            },
          },
          {
            ...pending.event,
            data: {
              ...pending.event.data,
              listPrice: { ...pending.event.data.listPrice, total: '60.00' },
            },
          },
        ]) {
          const invalid = {
            ...valid,
            paymentResult: { id: pending.row.orderNo, event },
          };
          assert.throws(
            () => validateOptimizerPayment(pending.row, invalid, 'waffo'),
            /authoritative order/
          );
          assert.equal(
            isEligibleOptimizerOrder({
              ...pending.row,
              status: 'paid',
              paymentAmount: OPTIMIZER_PRICE_IN_CENTS,
              paymentCurrency: 'usd',
              transactionId: valid.paymentInfo!.transactionId!,
              paymentResult: JSON.stringify(invalid.paymentResult),
            }),
            false
          );
        }
        const wrongRefund = waffoRefund(pending.event);
        wrongRefund.data.originalChargedAmount = '58.77';
        await assert.rejects(
          handlePaymentRefund(
            (await verifiedWaffoEvent(wrongRefund)).paymentSession!,
            'waffo'
          ),
          /does not match/
        );
        await handleCheckoutSuccess(valid, 'waffo');
        assert.ok(await service.fulfillOrder(pending.row.id));
      }
    );

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
              amount: OPTIMIZER_PRICE_IN_CENTS,
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
              amount: OPTIMIZER_PRICE_IN_CENTS,
              amount_refunded: OPTIMIZER_PRICE_IN_CENTS,
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
        assert.equal(
          validateOptimizerRelease({
            version: '0.3.0',
            downloadUrl: '/downloads/optimizer.zip',
            sha256: 'a'.repeat(64),
            sizeBytes: 32,
            activationAvailable: false,
          })?.activationAvailable,
          false
        );
      }
    );

    await t.test(
      'repricing keeps previous quotes valid for payment and refund',
      async () => {
        const previousStripe = await purchase('created', 5777);
        await handleCheckoutSuccess(previousStripe.session, 'stripe');
        assert.ok(await service.fulfillOrder(previousStripe.row.id));

        const previousWaffo = await waffoPurchase(5777);
        await handleCheckoutSuccess(
          (await verifiedWaffoEvent(previousWaffo.event)).paymentSession!,
          'waffo'
        );
        const license = (await service.fulfillOrder(previousWaffo.row.id))!;
        assert.ok(license);
        const refund = await verifiedWaffoEvent(
          waffoRefund(previousWaffo.event)
        );
        const refunded = await handlePaymentRefund(
          refund.paymentSession!,
          'waffo'
        );
        assert.deepEqual(refunded, [previousWaffo.row.id]);
        for (const id of refunded) await service.revokeOrder(id);
        assert.equal(await service.fulfillOrder(previousWaffo.row.id), null);
        assert.equal(
          (await service.list(buyer)).find((item) => item.id === license.id)
            ?.status,
          'refunded'
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

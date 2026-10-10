import assert from 'node:assert/strict';
import { createHash, generateKeyPairSync, sign, verify } from 'node:crypto';
import test from 'node:test';

import {
  PaymentEventType,
  PaymentStatus,
  PaymentType,
  WebhookIgnoredError,
  type PaymentOrder,
} from '../src/core/payment/types';
import {
  waffoAmountInCents,
  WaffoProvider,
  type WaffoConfigs,
  type WaffoWebhookEvent,
} from '../src/core/payment/waffo';

function keyPair() {
  return generateKeyPairSync('rsa', {
    modulusLength: 2048,
    publicKeyEncoding: { type: 'spki', format: 'pem' },
    privateKeyEncoding: { type: 'pkcs8', format: 'pem' },
  });
}

// Generate throwaway keys; fixtures never contain usable merchant credentials.
const merchantKeys = keyPair();
const webhookKeys = keyPair();
const differentWebhookKeys = keyPair();
const configs: WaffoConfigs = {
  merchantId: 'MER_testmerchant',
  privateKey: merchantKeys.privateKey,
  storeId: 'STO_teststore',
  productId: 'PROD_testproduct',
  webhookPublicKey: webhookKeys.publicKey,
  environment: 'test',
};

function dashboardKey(pem: string): string {
  return pem.replace(/-----[^-]+-----/g, '').replace(/\s/g, '');
}

function order(overrides: Partial<PaymentOrder> = {}): PaymentOrder {
  return {
    type: PaymentType.ONE_TIME,
    orderNo: 'order-001',
    productId: configs.productId,
    price: { amount: 2900, currency: 'usd' },
    successUrl: 'https://example.com/api/payment/callback/waffo',
    cancelUrl: 'https://example.com/pricing',
    customer: { email: 'buyer@example.com' },
    metadata: { orderId: 'internal-order-001', userId: 'user-001' },
    ...overrides,
  };
}

function nativePayment(): WaffoWebhookEvent {
  return {
    id: 'PAY_payment001',
    timestamp: new Date().toISOString(),
    eventId: 'PAY_payment001',
    eventType: 'order.completed',
    storeId: configs.storeId,
    storeName: 'Test store',
    mode: 'test',
    data: {
      orderId: 'ORD_native001',
      orderStatus: 'completed',
      orderMerchantExternalId: 'order-001',
      orderMetadata: {
        orderId: 'internal-order-001',
        userId: 'user-001',
        waffoProductId: configs.productId,
      },
      productMetadata: {},
      productName: 'Test product',
      buyerEmail: 'buyer@example.com',
      currency: 'USD',
      paymentId: 'PAY_payment001',
      paymentStatus: 'succeeded',
      paymentDate: '2026-10-10',
      chargedAmount: '29.00',
      // Legacy/list values deliberately differ from the actual charge.
      amount: '99.00',
      total: '99.00',
      listPrice: { total: '99.00' },
    },
  };
}

function nativeRefund(): WaffoWebhookEvent {
  const payment = nativePayment();
  const { chargedAmount: _, ...data } = payment.data;
  return {
    ...payment,
    id: 'REF_refund001',
    eventId: 'REF_refund001',
    eventType: 'refund.succeeded',
    data: {
      ...data,
      refundStatus: 'succeeded',
      refundedAmount: '10.00',
      originalChargedAmount: '29.00',
      originalPayment: { total: '99.00' },
      refundCreatedAt: new Date().toISOString(),
    },
  };
}

function signedHeader(
  body: string,
  timestamp = Date.now(),
  privateKey = webhookKeys.privateKey
): string {
  const signature = sign(
    'RSA-SHA256',
    Buffer.from(`${timestamp}.${body}`),
    privateKey
  ).toString('base64');
  return `t=${timestamp},v1=${signature}`;
}

function webhook(
  event: unknown,
  options: { timestamp?: number; privateKey?: string; signature?: string } = {}
): Request {
  const body = typeof event === 'string' ? event : JSON.stringify(event);
  return new Request('https://example.com/api/payment/webhook/waffo', {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'X-Waffo-Signature':
        options.signature ??
        signedHeader(body, options.timestamp, options.privateKey),
    },
    body,
  });
}

const checkoutResponse = {
  data: {
    sessionId: 'cs_550e8400-e29b-41d4-a716-446655440000',
    checkoutUrl:
      'https://checkout.waffo.ai/test/checkout/cs_550e8400-e29b-41d4-a716-446655440000',
    expiresAt: '2026-10-11T10:00:00.000Z',
  },
};

test('checkout signs the exact REST body and keeps the external order as session identity', async (t) => {
  const requests: RequestInit[] = [];
  t.mock.method(
    globalThis,
    'fetch',
    async (url: RequestInfo | URL, init: RequestInit) => {
      assert.equal(
        url,
        'https://api.waffo.ai/v1/actions/checkout/create-session'
      );
      requests.push(init);
      return Response.json(checkoutResponse);
    }
  );
  const provider = new WaffoProvider({
    ...configs,
    privateKey: dashboardKey(configs.privateKey),
    webhookPublicKey: dashboardKey(configs.webhookPublicKey),
  });
  const purchase = order({
    metadata: {
      orderId: 'internal-order-001',
      waffoProductId: 'untrusted-value',
    },
  });
  const result = await provider.createPayment({ order: purchase });

  const request = requests[0];
  const headers = new Headers(request.headers);
  const body = request.body as string;
  assert.equal(request.method, 'POST');
  assert.equal(request.redirect, 'error');
  assert.ok(request.signal instanceof AbortSignal);
  assert.equal(request.signal.aborted, false);
  assert.equal(headers.get('content-type'), 'application/json');
  assert.equal(headers.get('x-merchant-id'), configs.merchantId);
  assert.equal(headers.get('x-environment'), null);
  const timestamp = headers.get('x-timestamp')!;
  assert.match(timestamp, /^\d{10}$/);
  assert.ok(Math.abs(Number(timestamp) * 1000 - Date.now()) < 5_000);
  const canonical = [
    'POST',
    '/v1/actions/checkout/create-session',
    timestamp,
    createHash('sha256').update(body).digest('base64'),
  ].join('\n');
  assert.equal(
    verify(
      'RSA-SHA256',
      Buffer.from(canonical),
      merchantKeys.publicKey,
      Buffer.from(headers.get('x-signature')!, 'base64')
    ),
    true
  );
  assert.deepEqual(JSON.parse(body), {
    productId: configs.productId,
    currency: 'USD',
    priceSnapshot: {
      amount: '29.00',
      taxIncluded: true,
      taxCategory: 'digital_goods',
    },
    buyerEmail: 'buyer@example.com',
    successUrl: purchase.successUrl,
    metadata: {
      orderId: 'internal-order-001',
      waffoProductId: configs.productId,
    },
    orderMerchantExternalId: 'order-001',
  });
  assert.equal(result.provider, 'waffo');
  assert.deepEqual(result.checkoutInfo, {
    sessionId: 'order-001',
    checkoutUrl: checkoutResponse.data.checkoutUrl,
  });
  assert.deepEqual(result.checkoutResult, {
    ...checkoutResponse.data,
    orderNo: 'order-001',
    storeId: configs.storeId,
    mode: 'test',
    productId: configs.productId,
  });
  assert.equal(result.metadata.waffoProductId, configs.productId);

  await provider.createPayment({ order: purchase });
  await provider.createPayment({ order: order({ orderNo: 'order-002' }) });
  const firstIdempotencyKey = headers.get('x-idempotency-key');
  assert.match(firstIdempotencyKey!, /^[A-Za-z0-9_-]{1,256}$/);
  assert.equal(
    new Headers(requests[1].headers).get('x-idempotency-key'),
    firstIdempotencyKey
  );
  assert.notEqual(
    new Headers(requests[2].headers).get('x-idempotency-key'),
    firstIdempotencyKey
  );
});

test('checkout formats cents exactly, including the largest safe integer', async (t) => {
  let sentAmount: string | undefined;
  t.mock.method(
    globalThis,
    'fetch',
    async (_url: RequestInfo | URL, init: RequestInit) => {
      sentAmount = JSON.parse(init.body as string).priceSnapshot.amount;
      return Response.json(checkoutResponse);
    }
  );
  const provider = new WaffoProvider(configs);
  for (const [cents, expected] of [
    [1, '0.01'],
    [101, '1.01'],
    [Number.MAX_SAFE_INTEGER, '90071992547409.91'],
  ] as const) {
    await provider.createPayment({
      order: order({ price: { amount: cents, currency: 'USD' } }),
    });
    assert.equal(sentAmount, expected);
  }
});

test('invalid merchant, product, environment and RSA credentials are rejected', () => {
  for (const overrides of [
    { merchantId: '' },
    { storeId: '' },
    { productId: '' },
    { productId: 'subscription-plan' },
    { environment: 'production' },
    { privateKey: '' },
    { privateKey: 'not a key' },
    { webhookPublicKey: '' },
    { webhookPublicKey: 'not a key' },
  ]) {
    assert.throws(
      () => new WaffoProvider({ ...configs, ...overrides } as WaffoConfigs),
      /Waffo/
    );
  }
  const ecKeys = generateKeyPairSync('ec', {
    namedCurve: 'prime256v1',
    publicKeyEncoding: { type: 'spki', format: 'pem' },
    privateKeyEncoding: { type: 'pkcs8', format: 'pem' },
  });
  assert.throws(
    () => new WaffoProvider({ ...configs, privateKey: ecKeys.privateKey }),
    /RSA private key/
  );
  assert.throws(
    () => new WaffoProvider({ ...configs, webhookPublicKey: ecKeys.publicKey }),
    /RSA webhook public key/
  );
  assert.doesNotThrow(
    () =>
      new WaffoProvider({
        ...configs,
        privateKey: configs.privateKey.replace(/\n/g, '\\n'),
        webhookPublicKey: configs.webhookPublicKey.replace(/\n/g, '\\n'),
      })
  );
});

test('dashboard Base64 public keys verify real signed notifications', async () => {
  const provider = new WaffoProvider({
    ...configs,
    privateKey: dashboardKey(configs.privateKey),
    webhookPublicKey: dashboardKey(configs.webhookPublicKey),
  });
  const event = await provider.getPaymentEvent({
    req: webhook(nativePayment()),
  });
  assert.equal(event.eventType, PaymentEventType.CHECKOUT_SUCCESS);
  assert.equal(event.paymentSession?.paymentStatus, PaymentStatus.SUCCESS);
});

test('unsupported and malformed orders fail before sending an API request', async (t) => {
  const fetchMock = t.mock.method(globalThis, 'fetch', async () => {
    assert.fail('An invalid order must never reach Waffo');
  });
  const provider = new WaffoProvider(configs);
  const invalidOrders: Partial<PaymentOrder>[] = [
    { type: PaymentType.SUBSCRIPTION },
    { type: PaymentType.RENEW },
    { type: undefined },
    { orderNo: '' },
    { orderNo: ' order-001 ' },
    { orderNo: 'x'.repeat(129) },
    { productId: 'PROD_other' },
    { productId: undefined },
    { price: undefined },
    { price: { amount: 2900, currency: 'eur' } },
    ...[0, -1, 29.01, NaN, Infinity, Number.MAX_SAFE_INTEGER + 1].map(
      (amount) => ({ price: { amount, currency: 'usd' } })
    ),
    { successUrl: undefined },
    { successUrl: '/success' },
    { successUrl: 'https:example.com/success' },
    { successUrl: 'http://localhost:3000/success' },
    { successUrl: 'javascript:alert(1)' },
    { successUrl: 'https://user:password@example.com/success' },
    { quantity: 2 },
    { discount: { code: 'UNSUPPORTED' } },
    { metadata: [] },
  ];
  for (const invalidOrder of invalidOrders) {
    await assert.rejects(
      provider.createPayment({ order: order(invalidOrder) }),
      /Waffo/
    );
  }
  assert.equal(fetchMock.mock.callCount(), 0);
});

test('checkout rejects API failures, ambiguous envelopes and unsafe destinations', async (t) => {
  const provider = new WaffoProvider(configs);
  const responseFactories = [
    () => new Response('Unauthorized', { status: 401 }),
    () => new Response('not JSON', { status: 200 }),
    () => Response.json(null),
    () => Response.json(checkoutResponse.data),
    () => Response.json({ data: null }),
    () =>
      Response.json({ ...checkoutResponse, errors: [{ message: 'failed' }] }),
    () => Response.json({ ...checkoutResponse, errors: 'failed' }),
    () =>
      Response.json({
        data: { ...checkoutResponse.data, sessionId: '' },
      }),
    ...['/checkout', 'http://example.com/checkout', 'javascript:alert(1)'].map(
      (checkoutUrl) => () =>
        Response.json({ data: { ...checkoutResponse.data, checkoutUrl } })
    ),
  ];
  t.mock.method(globalThis, 'fetch', async () => responseFactories.shift()!());
  while (responseFactories.length > 0) {
    await assert.rejects(provider.createPayment({ order: order() }), /Waffo/);
  }
});

test('browser return remains processing and cannot manufacture proof of payment', async (t) => {
  const fetchMock = t.mock.method(globalThis, 'fetch', async () => {
    assert.fail('There is no documented checkout session read endpoint');
  });
  const result = await new WaffoProvider(configs).getPaymentSession({
    sessionId: 'order-001',
  });
  assert.deepEqual(result, {
    provider: 'waffo',
    paymentStatus: PaymentStatus.PROCESSING,
    paymentResult: { id: 'order-001' },
  });
  assert.equal(fetchMock.mock.callCount(), 0);
});

test('a signed one-time payment uses actual charged cents and preserves its proof', async () => {
  const event = nativePayment();
  const result = await new WaffoProvider(configs).getPaymentEvent({
    req: webhook(event),
  });
  assert.equal(result.eventType, PaymentEventType.CHECKOUT_SUCCESS);
  assert.deepEqual(result.eventResult, event);
  assert.deepEqual(result.paymentSession, {
    provider: 'waffo',
    paymentStatus: PaymentStatus.SUCCESS,
    paymentInfo: {
      transactionId: 'PAY_payment001',
      paymentAmount: 2900,
      paymentCurrency: 'usd',
      paymentEmail: 'buyer@example.com',
      paidAt: new Date('2026-10-10'),
    },
    paymentResult: { id: 'order-001', event },
    metadata: event.data.orderMetadata,
  });
});

test('a signed refund uses refunded cents and keeps original actual charged amount', async () => {
  const event = nativeRefund();
  const result = await new WaffoProvider(configs).getPaymentEvent({
    req: webhook(event),
  });
  assert.equal(result.eventType, PaymentEventType.PAYMENT_REFUNDED);
  assert.equal(result.paymentSession?.paymentInfo?.paymentAmount, 1000);
  assert.equal(
    result.paymentSession?.paymentInfo?.transactionId,
    'PAY_payment001'
  );
  assert.deepEqual(result.paymentSession?.paymentResult, {
    id: 'order-001',
    event,
  });
  assert.equal(
    result.paymentSession?.paymentResult.event.data.originalChargedAmount,
    '29.00'
  );
});

test('unsigned, forged and tampered webhook bodies never become payment events', async () => {
  const provider = new WaffoProvider(configs);
  const event = nativePayment();
  const body = JSON.stringify(event);
  const signature = signedHeader(body);
  const requests = [
    new Request('https://example.com/webhook', { method: 'POST', body }),
    webhook(event, { privateKey: differentWebhookKeys.privateKey }),
    webhook(body.replace('29.00', '99.00'), { signature }),
    webhook(JSON.stringify(event, null, 2), { signature }),
    webhook(event, { signature: `t=${Date.now()},v1=Zm9yZ2Vk` }),
  ];
  for (const req of requests) {
    await assert.rejects(provider.getPaymentEvent({ req }), /signature/);
  }
});

test('duplicate, missing and malformed signature header fields are rejected', async () => {
  const provider = new WaffoProvider(configs);
  const event = nativePayment();
  const signature = signedHeader(JSON.stringify(event));
  const [timestampPart, valuePart] = signature.split(',');
  for (const header of [
    `${signature},${timestampPart}`,
    `${signature},${valuePart}`,
    `${signature},unknown=x`,
    `${signature},`,
    `${timestampPart}`,
    `${valuePart}`,
    `t=1.23,${valuePart}`,
    `t=${Math.floor(Date.now() / 1000)},${valuePart}`,
    `${timestampPart},v1=!!!!`,
  ]) {
    await assert.rejects(
      provider.getPaymentEvent({ req: webhook(event, { signature: header }) }),
      /signature header/
    );
  }
});

test('original signatures survive delayed retries but stale or future events fail', async () => {
  const provider = new WaffoProvider(configs);
  const event = nativePayment();
  const validRetry = await provider.getPaymentEvent({
    req: webhook(event, { timestamp: Date.now() - 32 * 60 * 1000 }),
  });
  assert.equal(validRetry.eventType, PaymentEventType.CHECKOUT_SUCCESS);
  for (const timestamp of [
    Date.now() - 46 * 60 * 1000,
    Date.now() + 2 * 60 * 1000,
  ]) {
    await assert.rejects(
      provider.getPaymentEvent({ req: webhook(event, { timestamp }) }),
      /timestamp/
    );
  }
});

test('webhook verification binds the configured key, environment and store', async () => {
  const provider = new WaffoProvider(configs);
  const production = { ...nativePayment(), mode: 'prod' };
  await assert.rejects(
    provider.getPaymentEvent({ req: webhook(production) }),
    /environment or store mismatch/
  );
  await assert.rejects(
    provider.getPaymentEvent({
      req: webhook({ ...nativePayment(), storeId: 'STO_other' }),
    }),
    /environment or store mismatch/
  );
  await assert.rejects(
    provider.getPaymentEvent({
      req: webhook(production, { privateKey: differentWebhookKeys.privateKey }),
    }),
    /Invalid Waffo webhook signature/
  );
});

test('authentic unsupported events are ignored only after signature and scope checks', async () => {
  const provider = new WaffoProvider(configs);
  const event = { ...nativePayment(), eventType: 'subscription.activated' };
  await assert.rejects(
    provider.getPaymentEvent({ req: webhook(event) }),
    WebhookIgnoredError
  );
  await assert.rejects(
    provider.getPaymentEvent({
      req: webhook(event, { privateKey: differentWebhookKeys.privateKey }),
    }),
    /Invalid Waffo webhook signature/
  );
  await assert.rejects(
    provider.getPaymentEvent({
      req: webhook({ ...event, mode: 'prod' }),
    }),
    /environment or store mismatch/
  );
});

test('signed malformed or unsuccessful payment payloads fail closed', async () => {
  const provider = new WaffoProvider(configs);
  const event = nativePayment();
  for (const invalid of [
    'not JSON',
    null,
    [],
    { ...event, id: '' },
    { ...event, eventId: '' },
    { ...event, timestamp: 'not a date' },
    { ...event, data: null },
    ...[
      { orderId: '' },
      { orderMerchantExternalId: '' },
      { paymentId: '' },
      { buyerEmail: '' },
      { orderMetadata: [] },
      { currency: 'EUR' },
      { paymentStatus: 'failed' },
      { orderStatus: 'pending' },
      { chargedAmount: undefined },
      { chargedAmount: 29 },
      { chargedAmount: '0.00' },
      { chargedAmount: '29.001' },
      { paymentDate: 'invalid' },
      { paymentDate: '2026-02-30' },
    ].map((data) => ({ ...event, data: { ...event.data, ...data } })),
  ]) {
    await assert.rejects(
      provider.getPaymentEvent({ req: webhook(invalid) }),
      /Waffo/
    );
  }
});

test('refunds require reported original charges, successful status and valid refunded cents', async () => {
  const provider = new WaffoProvider(configs);
  const event = nativeRefund();
  for (const data of [
    { refundedAmount: undefined },
    { originalChargedAmount: undefined },
    { originalChargedAmount: '0.00' },
    { refundedAmount: '30.00' },
    { refundedAmount: '0.00' },
    { refundedAmount: '-10.00' },
    { refundStatus: 'failed' },
    { paymentStatus: 'failed' },
    { orderStatus: 'pending' },
  ]) {
    await assert.rejects(
      provider.getPaymentEvent({
        req: webhook({ ...event, data: { ...event.data, ...data } }),
      }),
      /Waffo/
    );
  }
});

test('USD decimal parser accepts exact cents and rejects ambiguous or unsafe amounts', () => {
  for (const [amount, cents] of [
    ['0', 0],
    ['0.0', 0],
    ['0.01', 1],
    ['1', 100],
    ['1.1', 110],
    ['29.00', 2900],
    ['90071992547409.91', Number.MAX_SAFE_INTEGER],
  ] as const) {
    assert.equal(waffoAmountInCents(amount), cents);
  }
  for (const value of [
    undefined,
    null,
    29,
    NaN,
    '',
    ' 29.00',
    '29.00 ',
    '01.00',
    '+29.00',
    '-29.00',
    '29.',
    '.29',
    '1e2',
    '1,000.00',
    '29.001',
    'Infinity',
    '90071992547409.92',
    '999999999999999999999999999999',
  ]) {
    assert.throws(() => waffoAmountInCents(value), /Waffo USD amount/);
  }
});

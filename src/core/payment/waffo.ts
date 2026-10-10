import {
  createHash,
  createPrivateKey,
  createPublicKey,
  sign,
  verify,
  type KeyObject,
} from 'node:crypto';

import {
  PaymentEventType,
  PaymentStatus,
  PaymentType,
  WebhookIgnoredError,
  type CheckoutSession,
  type PaymentConfigs,
  type PaymentEvent,
  type PaymentOrder,
  type PaymentProvider,
  type PaymentSession,
} from './types';

const CHECKOUT_PATH = '/v1/actions/checkout/create-session';
const WEBHOOK_MAX_AGE_MS = 45 * 60 * 1000;
const WEBHOOK_FUTURE_TOLERANCE_MS = 60 * 1000;

export interface WaffoConfigs extends PaymentConfigs {
  merchantId: string;
  privateKey: string;
  storeId: string;
  productId: string;
  webhookPublicKey: string;
  environment: 'test' | 'prod';
}

export interface WaffoWebhookData extends Record<string, unknown> {
  orderId: string;
  orderStatus: string;
  orderMerchantExternalId: string;
  orderMetadata: Record<string, unknown>;
  buyerEmail: string;
  currency: string;
  paymentId: string;
  paymentStatus: string;
  paymentDate?: string;
  chargedAmount?: string;
  refundedAmount?: string;
  originalChargedAmount?: string;
  refundStatus?: string;
}

/** The complete signed payload is retained for order-level reconciliation. */
export interface WaffoWebhookEvent extends Record<string, unknown> {
  id: string;
  timestamp: string;
  eventId: string;
  eventType: 'order.completed' | 'refund.succeeded';
  storeId: string;
  mode: 'test' | 'prod';
  data: WaffoWebhookData;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function isNonemptyString(value: unknown): value is string {
  return typeof value === 'string' && value.trim().length > 0;
}

function isHttpsUrl(value: unknown): value is string {
  if (
    !isNonemptyString(value) ||
    value !== value.trim() ||
    !/^https:\/\//i.test(value)
  ) {
    return false;
  }
  try {
    const url = new URL(value);
    return url.protocol === 'https:' && !url.username && !url.password;
  } catch {
    return false;
  }
}

function normalizePem(value: string, kind: 'PRIVATE' | 'PUBLIC'): string {
  const normalized = value
    .replace(/\\r\\n/g, '\n')
    .replace(/\\n/g, '\n')
    .trim();
  // Pancake displays PKCS#8 / SPKI keys as bare Base64 in its dashboard.
  if (/^[A-Za-z0-9+/=\s]+$/.test(normalized)) {
    const lines = normalized.replace(/\s/g, '').match(/.{1,64}/g);
    return `-----BEGIN ${kind} KEY-----\n${lines?.join('\n') ?? ''}\n-----END ${kind} KEY-----`;
  }
  return normalized;
}

/** Parse unsigned USD display amounts without rounding or floating-point math. */
export function waffoAmountInCents(value: unknown): number {
  if (
    typeof value !== 'string' ||
    value.length > 18 ||
    !/^(0|[1-9]\d*)(?:\.\d{1,2})?$/.test(value)
  ) {
    throw new Error('Invalid Waffo USD amount');
  }

  const [whole, fraction = ''] = value.split('.');
  const cents = BigInt(whole) * 100n + BigInt(fraction.padEnd(2, '0'));
  if (cents > BigInt(Number.MAX_SAFE_INTEGER)) {
    throw new Error('Waffo USD amount exceeds the safe integer range');
  }
  return Number(cents);
}

/**
 * Waffo Pancake REST integration for one-time USD purchases.
 * @docs https://docs.waffo.ai/api-reference/authentication
 * @docs https://docs.waffo.ai/api-reference/webhooks
 */
export class WaffoProvider implements PaymentProvider {
  readonly name = 'waffo';
  configs: WaffoConfigs;
  private readonly privateKey: KeyObject;
  private readonly webhookPublicKey: KeyObject;

  constructor(configs: WaffoConfigs) {
    if (
      !/^MER_[a-zA-Z0-9]+$/.test(configs.merchantId) ||
      !/^STO_[a-zA-Z0-9]+$/.test(configs.storeId) ||
      !/^PROD_[a-zA-Z0-9]+$/.test(configs.productId) ||
      !['test', 'prod'].includes(configs.environment) ||
      !isNonemptyString(configs.privateKey) ||
      !isNonemptyString(configs.webhookPublicKey)
    ) {
      throw new Error('Waffo payment configuration is incomplete or invalid');
    }

    this.configs = {
      ...configs,
      privateKey: normalizePem(configs.privateKey, 'PRIVATE'),
      webhookPublicKey: normalizePem(configs.webhookPublicKey, 'PUBLIC'),
    };

    try {
      this.privateKey = createPrivateKey(this.configs.privateKey);
      if (this.privateKey.asymmetricKeyType !== 'rsa') throw new Error();
    } catch {
      throw new Error('Invalid Waffo RSA private key');
    }
    try {
      this.webhookPublicKey = createPublicKey(this.configs.webhookPublicKey);
      if (this.webhookPublicKey.asymmetricKeyType !== 'rsa') throw new Error();
    } catch {
      throw new Error('Invalid Waffo RSA webhook public key');
    }
  }

  async createPayment({
    order,
  }: {
    order: PaymentOrder;
  }): Promise<CheckoutSession> {
    if (order.type !== PaymentType.ONE_TIME || order.plan) {
      throw new Error('Waffo supports one-time payments only');
    }
    if (order.quantity !== undefined && order.quantity !== 1) {
      throw new Error('Waffo supports a single product per order');
    }
    if (order.discount) {
      throw new Error('Waffo discount codes are not supported');
    }
    if (
      !isNonemptyString(order.orderNo) ||
      order.orderNo !== order.orderNo.trim() ||
      order.orderNo.length > 128 ||
      order.productId !== this.configs.productId
    ) {
      throw new Error(
        'Waffo requires a valid order number and configured product'
      );
    }
    if (
      !order.price ||
      typeof order.price.currency !== 'string' ||
      order.price.currency.toLowerCase() !== 'usd' ||
      !Number.isSafeInteger(order.price.amount) ||
      order.price.amount <= 0
    ) {
      throw new Error('Waffo requires a positive USD price in integer cents');
    }
    if (!isHttpsUrl(order.successUrl)) {
      throw new Error('Waffo success URL must be an absolute HTTPS URL');
    }
    if (order.metadata !== undefined && !isRecord(order.metadata)) {
      throw new Error('Invalid Waffo order metadata');
    }

    const amount = BigInt(order.price.amount);
    const metadata = {
      ...order.metadata,
      waffoProductId: this.configs.productId,
    };
    const payload = {
      productId: this.configs.productId,
      currency: 'USD',
      priceSnapshot: {
        amount: `${amount / 100n}.${(amount % 100n).toString().padStart(2, '0')}`,
        taxIncluded: true,
        taxCategory: 'digital_goods',
      },
      buyerEmail: order.customer?.email,
      successUrl: order.successUrl,
      metadata,
      orderMerchantExternalId: order.orderNo,
    };
    const body = JSON.stringify(payload);
    const timestamp = Math.floor(Date.now() / 1000).toString();
    const bodyHash = createHash('sha256').update(body).digest('base64');
    const canonical = `POST\n${CHECKOUT_PATH}\n${timestamp}\n${bodyHash}`;
    const signature = sign(
      'RSA-SHA256',
      Buffer.from(canonical),
      this.privateKey
    );
    const idempotencyKey = createHash('sha256')
      .update(
        JSON.stringify([
          this.configs.merchantId,
          this.configs.environment,
          this.configs.storeId,
          CHECKOUT_PATH,
          order.orderNo,
        ])
      )
      .digest('hex');

    const response = await fetch(`https://api.waffo.ai${CHECKOUT_PATH}`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'X-Merchant-Id': this.configs.merchantId,
        'X-Timestamp': timestamp,
        'X-Signature': signature.toString('base64'),
        'X-Idempotency-Key': `waffo-checkout-${idempotencyKey}`,
      },
      body,
      signal: AbortSignal.timeout(15_000),
      redirect: 'error',
    });
    if (!response.ok) {
      throw new Error(`Waffo checkout request failed (${response.status})`);
    }

    let result: unknown;
    try {
      result = await response.json();
    } catch {
      throw new Error('Invalid Waffo checkout response');
    }
    if (
      !isRecord(result) ||
      (result.errors != null &&
        (!Array.isArray(result.errors) || result.errors.length > 0)) ||
      !isRecord(result.data) ||
      !isNonemptyString(result.data.sessionId) ||
      !result.data.sessionId.startsWith('cs_') ||
      !isHttpsUrl(result.data.checkoutUrl)
    ) {
      throw new Error('Invalid Waffo checkout response');
    }

    return {
      provider: this.name,
      checkoutParams: payload,
      checkoutInfo: {
        // Webhooks include our external order number, not the checkout session ID.
        sessionId: order.orderNo,
        checkoutUrl: result.data.checkoutUrl,
      },
      checkoutResult: {
        ...result.data,
        orderNo: order.orderNo,
        storeId: this.configs.storeId,
        mode: this.configs.environment,
        productId: this.configs.productId,
      },
      metadata,
    };
  }

  async getPaymentSession({
    sessionId,
  }: {
    sessionId: string;
  }): Promise<PaymentSession> {
    if (!isNonemptyString(sessionId)) {
      throw new Error('Waffo payment session ID is required');
    }
    // A browser return never proves payment. The verified webhook settles it.
    return {
      provider: this.name,
      paymentStatus: PaymentStatus.PROCESSING,
      paymentResult: { id: sessionId },
    };
  }

  async getPaymentEvent({ req }: { req: Request }): Promise<PaymentEvent> {
    const rawBody = await req.text();
    this.verifyWebhook(rawBody, req.headers.get('X-Waffo-Signature'));

    let parsed: unknown;
    try {
      parsed = JSON.parse(rawBody);
    } catch {
      throw new Error('Invalid Waffo webhook payload');
    }
    if (
      !isRecord(parsed) ||
      !isNonemptyString(parsed.id) ||
      !isNonemptyString(parsed.eventId) ||
      !isNonemptyString(parsed.eventType) ||
      !isNonemptyString(parsed.timestamp) ||
      !Number.isFinite(Date.parse(parsed.timestamp)) ||
      !isRecord(parsed.data)
    ) {
      throw new Error('Invalid Waffo webhook payload');
    }
    if (
      parsed.mode !== this.configs.environment ||
      parsed.storeId !== this.configs.storeId
    ) {
      throw new Error('Waffo webhook environment or store mismatch');
    }
    if (
      parsed.eventType !== 'order.completed' &&
      parsed.eventType !== 'refund.succeeded'
    ) {
      throw new WebhookIgnoredError('Unhandled Waffo webhook event');
    }

    const data = parsed.data;
    if (
      !isNonemptyString(data.orderId) ||
      !isNonemptyString(data.orderMerchantExternalId) ||
      data.orderMerchantExternalId.length > 128 ||
      !isNonemptyString(data.paymentId) ||
      !isNonemptyString(data.buyerEmail) ||
      !isRecord(data.orderMetadata) ||
      typeof data.currency !== 'string' ||
      data.currency.toUpperCase() !== 'USD' ||
      data.orderStatus !== 'completed' ||
      data.paymentStatus !== 'succeeded'
    ) {
      throw new Error('Invalid Waffo payment data');
    }

    const isRefund = parsed.eventType === 'refund.succeeded';
    const paymentAmount = waffoAmountInCents(
      isRefund ? data.refundedAmount : data.chargedAmount
    );
    if (paymentAmount <= 0) {
      throw new Error('Waffo payment amount must be positive');
    }
    if (isRefund) {
      const originalAmount = waffoAmountInCents(data.originalChargedAmount);
      if (data.refundStatus !== 'succeeded' || paymentAmount > originalAmount) {
        throw new Error('Invalid Waffo refund data');
      }
    }

    let paidAt: Date | undefined;
    if (data.paymentDate !== undefined) {
      if (
        typeof data.paymentDate !== 'string' ||
        !/^\d{4}-\d{2}-\d{2}$/.test(data.paymentDate) ||
        !Number.isFinite(Date.parse(data.paymentDate))
      ) {
        throw new Error('Invalid Waffo payment date');
      }
      paidAt = new Date(data.paymentDate);
      if (paidAt.toISOString().slice(0, 10) !== data.paymentDate) {
        throw new Error('Invalid Waffo payment date');
      }
    }

    const event = parsed as WaffoWebhookEvent;
    return {
      eventType: isRefund
        ? PaymentEventType.PAYMENT_REFUNDED
        : PaymentEventType.CHECKOUT_SUCCESS,
      eventResult: event,
      paymentSession: {
        provider: this.name,
        paymentStatus: PaymentStatus.SUCCESS,
        paymentInfo: {
          transactionId: data.paymentId,
          paymentAmount,
          paymentCurrency: 'usd',
          paymentEmail: data.buyerEmail,
          paidAt,
        },
        paymentResult: { id: data.orderMerchantExternalId, event },
        metadata: data.orderMetadata,
      },
    };
  }

  private verifyWebhook(rawBody: string, signatureHeader: string | null): void {
    if (!rawBody || !signatureHeader) {
      throw new Error('Missing Waffo webhook signature');
    }

    const parts = new Map<string, string>();
    for (const part of signatureHeader.split(',')) {
      const separator = part.indexOf('=');
      const key = part.slice(0, separator).trim();
      const value = part.slice(separator + 1).trim();
      if (
        separator <= 0 ||
        !['t', 'v1'].includes(key) ||
        !value ||
        parts.has(key)
      ) {
        throw new Error('Invalid Waffo webhook signature header');
      }
      parts.set(key, value);
    }

    const timestamp = parts.get('t');
    const encodedSignature = parts.get('v1');
    if (
      !timestamp ||
      !/^\d{13}$/.test(timestamp) ||
      !encodedSignature ||
      !/^[A-Za-z0-9+/]+={0,2}$/.test(encodedSignature)
    ) {
      throw new Error('Invalid Waffo webhook signature header');
    }
    const age = Date.now() - Number(timestamp);
    if (age > WEBHOOK_MAX_AGE_MS || age < -WEBHOOK_FUTURE_TOLERANCE_MS) {
      throw new Error('Waffo webhook timestamp is outside the allowed window');
    }

    const signature = Buffer.from(encodedSignature, 'base64');
    if (
      signature.toString('base64') !== encodedSignature ||
      !verify(
        'RSA-SHA256',
        Buffer.from(`${timestamp}.${rawBody}`),
        this.webhookPublicKey,
        signature
      )
    ) {
      throw new Error('Invalid Waffo webhook signature');
    }
  }
}

export function createWaffoProvider(configs: WaffoConfigs): WaffoProvider {
  return new WaffoProvider(configs);
}

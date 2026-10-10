import type { PaymentInfo } from '@/core/payment/types';
import { waffoAmountInCents } from '@/core/payment/waffo';
import type { Order } from '@/config/db/schema';
import {
  OPTIMIZER_CURRENCY,
  OPTIMIZER_PRICE_IN_CENTS,
  OPTIMIZER_PRODUCT_ID,
} from '@/config/optimizer';

function catalogMatches(order: Order) {
  return (
    order.productId === OPTIMIZER_PRODUCT_ID &&
    order.paymentType === 'one-time' &&
    // Previously issued quotes remain payable/refundable after repricing.
    // New checkouts separately enforce only the current catalog price.
    (order.amount === OPTIMIZER_PRICE_IN_CENTS || order.amount === 5777) &&
    order.currency === OPTIMIZER_CURRENCY
  );
}

/** All data here is server-stored evidence, never browser return parameters. */
function record(value: unknown): Record<string, unknown> {
  return value !== null && typeof value === 'object' && !Array.isArray(value)
    ? (value as Record<string, unknown>)
    : {};
}

function hasId(value: unknown, prefix: string) {
  return (
    typeof value === 'string' &&
    new RegExp(`^${prefix}_[A-Za-z0-9]+$`).test(value)
  );
}

function matchesOptionalTotal(value: unknown, amount: number) {
  return value == null || waffoAmountInCents(record(value).total) === amount;
}

function waffoOrderMatches(
  order: Order,
  result: Record<string, unknown>,
  checkout: Record<string, unknown>
) {
  const event = record(result.event);
  const data = record(event.data);
  const metadata = record(data.orderMetadata);
  return Boolean(
    catalogMatches(order) &&
    order.paymentProvider === 'waffo' &&
    result.id === order.orderNo &&
    order.paymentSessionId === order.orderNo &&
    checkout.orderNo === order.orderNo &&
    typeof checkout.sessionId === 'string' &&
    checkout.sessionId.startsWith('cs_') &&
    (checkout.mode === 'test' || checkout.mode === 'prod') &&
    hasId(checkout.storeId, 'STO') &&
    hasId(checkout.productId, 'PROD') &&
    checkout.productId === order.paymentProductId &&
    event.mode === checkout.mode &&
    event.storeId === checkout.storeId &&
    hasId(data.orderId, 'ORD') &&
    hasId(data.paymentId, 'PAY') &&
    data.currency === 'USD' &&
    data.orderMerchantExternalId === order.orderNo &&
    metadata.orderNo === order.orderNo &&
    metadata.userId === order.userId &&
    metadata.productId === OPTIMIZER_PRODUCT_ID &&
    metadata.waffoProductId === checkout.productId &&
    metadata.licenseProduct === 'true'
  );
}

export function hasOptimizerPaymentProof(
  order: Order,
  paymentResult: unknown,
  info:
    | Pick<PaymentInfo, 'paymentAmount' | 'paymentCurrency' | 'transactionId'>
    | undefined,
  provider: string
): boolean {
  if (
    !catalogMatches(order) ||
    order.paymentProvider !== provider ||
    info?.paymentAmount !== order.amount ||
    info.paymentCurrency !== order.currency
  )
    return false;
  try {
    const checkout = record(JSON.parse(order.checkoutResult || '{}'));
    const result = record(paymentResult);
    if (provider === 'waffo') {
      const event = record(result.event);
      const data = record(event.data);
      return (
        waffoOrderMatches(order, result, checkout) &&
        event.eventType === 'order.completed' &&
        data.orderStatus === 'completed' &&
        data.paymentStatus === 'succeeded' &&
        data.paymentId === info.transactionId &&
        waffoAmountInCents(data.chargedAmount) === order.amount &&
        matchesOptionalTotal(data.listPrice, order.amount)
      );
    }
    return (
      provider === 'stripe' &&
      result.id === order.paymentSessionId &&
      result.mode === 'payment' &&
      result.payment_status === 'paid' &&
      result.amount_total === order.amount &&
      result.amount_subtotal === order.amount &&
      result.currency === order.currency &&
      typeof result.livemode === 'boolean' &&
      result.livemode === checkout.livemode &&
      record(result.metadata).orderNo === order.orderNo &&
      record(result.metadata).productId === OPTIMIZER_PRODUCT_ID &&
      (record(result.total_details).amount_discount ?? 0) === 0 &&
      (record(result.total_details).amount_tax ?? 0) === 0
    );
  } catch {
    return false;
  }
}

export function hasWaffoRefundProof(
  order: Order,
  paymentResult: unknown
): boolean {
  try {
    const checkout = record(JSON.parse(order.checkoutResult || '{}'));
    const result = record(paymentResult);
    const event = record(result.event);
    const data = record(event.data);
    return (
      waffoOrderMatches(order, result, checkout) &&
      event.eventType === 'refund.succeeded' &&
      data.refundStatus === 'succeeded' &&
      (!order.transactionId || data.paymentId === order.transactionId) &&
      waffoAmountInCents(data.originalChargedAmount) === order.amount &&
      matchesOptionalTotal(data.originalPayment, order.amount) &&
      waffoAmountInCents(data.refundedAmount) > 0 &&
      waffoAmountInCents(data.refundedAmount) <= order.amount
    );
  } catch {
    return false;
  }
}

import { and, desc, eq, inArray, isNull, or } from 'drizzle-orm';

import { db } from '@/core/db';
import {
  AlipayProvider,
  CreemProvider,
  PaymentManager,
  StripeProvider,
  WaffoProvider,
  WechatPayProvider,
} from '@/core/payment';
import {
  PaymentStatus,
  PaymentType,
  SubscriptionCycleType,
  type CheckoutSession,
  type PaymentEvent,
  type PaymentOrder,
  type PaymentSession,
} from '@/core/payment/types';
import { credit, order, subscription, type Order } from '@/config/db/schema';
import {
  OPTIMIZER_CURRENCY,
  OPTIMIZER_PRICE_IN_CENTS,
  OPTIMIZER_PRODUCT_ID,
} from '@/config/optimizer';
import { getAllConfigs } from '@/modules/config/service';
import { calculateCreditExpirationTime } from '@/modules/credits/service';
import {
  findByProviderSubscriptionId,
  findBySubscriptionNo,
  SubscriptionStatus,
  updateBySubscriptionNo,
  type NewSubscription,
  type UpdateSubscription,
} from '@/modules/subscriptions/service';
import { getSnowId, getUniSeq, getUuid } from '@/lib/hash';
import {
  hasOptimizerPaymentProof,
  hasWaffoRefundProof,
} from '@/lib/optimizer-payment-proof';

// --- Order types ---

enum OrderStatus {
  PENDING = 'pending',
  CREATED = 'created',
  PAID = 'paid',
  FAILED = 'failed',
  REFUNDED = 'refunded',
}

// --- Payment Manager ---

let manager: PaymentManager | null = null;
let managerConfigHash = '';

async function getPaymentManager(): Promise<PaymentManager> {
  const configs = await getAllConfigs();
  const c = (key: string) => configs[key] || '';

  // Rebuild manager if provider configs changed
  const hash = JSON.stringify(
    Object.entries(configs)
      .filter(([key]) =>
        /^(stripe|creem|alipay|wechat|waffo)_|^default_payment_provider$/.test(
          key
        )
      )
      .sort(([a], [b]) => a.localeCompare(b))
  );
  if (manager && hash === managerConfigHash) return manager;

  const nextManager = new PaymentManager();

  const stripeKey = c('stripe_secret_key') || c('stripe_api_key');
  if (stripeKey) {
    const isDefault =
      !c('default_payment_provider') ||
      c('default_payment_provider') === 'stripe';
    nextManager.addProvider(
      new StripeProvider({
        secretKey: stripeKey,
        publishableKey: c('stripe_publishable_key'),
        signingSecret:
          c('stripe_signing_secret') || c('stripe_webhook_secret') || undefined,
        allowPromotionCodes: true,
        allowedPaymentMethods: ['card', 'wechat_pay', 'alipay'],
      }),
      isDefault
    );
  }

  if (c('creem_enabled') === 'true' && c('creem_api_key')) {
    const isDefault = c('default_payment_provider') === 'creem';
    nextManager.addProvider(
      new CreemProvider({
        apiKey: c('creem_api_key'),
        signingSecret: c('creem_signing_secret') || undefined,
        environment:
          c('creem_environment') === 'production' ? 'production' : 'sandbox',
      }),
      isDefault
    );
  }

  if (c('alipay_app_id') && c('alipay_private_key')) {
    const isDefault = c('default_payment_provider') === 'alipay';
    nextManager.addProvider(
      new AlipayProvider({
        appId: c('alipay_app_id'),
        privateKey: c('alipay_private_key'),
        alipayPublicKey: c('alipay_public_key'),
        notifyUrl: c('alipay_notify_url') || undefined,
      }),
      isDefault
    );
  }

  if (c('wechat_mch_id') && c('wechat_private_key')) {
    const isDefault = c('default_payment_provider') === 'wechat';
    nextManager.addProvider(
      new WechatPayProvider({
        appId: c('wechat_app_id'),
        mchId: c('wechat_mch_id'),
        apiV3Key: c('wechat_api_v3_key'),
        privateKey: c('wechat_private_key'),
        serialNo: c('wechat_serial_no'),
        notifyUrl: c('wechat_notify_url') || undefined,
        platformCert: c('wechat_platform_cert') || undefined,
      }),
      isDefault
    );
  }

  if (c('waffo_enabled') === 'true') {
    try {
      nextManager.addProvider(
        new WaffoProvider({
          merchantId: c('waffo_merchant_id'),
          privateKey: c('waffo_private_key'),
          storeId: c('waffo_store_id'),
          productId: c('waffo_product_id'),
          webhookPublicKey: c('waffo_webhook_public_key'),
          environment: c('waffo_environment') as 'test' | 'prod',
        }),
        c('default_payment_provider') === 'waffo'
      );
    } catch {
      // An incomplete Waffo setup must not interrupt other providers' webhooks.
      // Explicit Waffo lookup still fails; the admin Test action reports details.
      console.warn(
        'Waffo payment provider configuration is incomplete or invalid'
      );
    }
  }

  manager = nextManager;
  managerConfigHash = hash;
  return manager;
}

// --- Checkout ---

export async function createCheckout(params: {
  userId: string;
  userEmail?: string;
  paymentOrder: PaymentOrder;
  provider?: string;
  productName?: string;
  planName?: string;
  credits?: number;
  creditsValidDays?: number;
}): Promise<CheckoutSession> {
  const {
    userId,
    userEmail,
    paymentOrder,
    provider,
    productName,
    planName,
    credits,
    creditsValidDays,
  } = params;
  const pm = await getPaymentManager();
  const orderNo = getUniSeq('ORD');
  const configs = await getAllConfigs();
  const appUrl = configs.app_url || 'http://localhost:3000';

  // Resolve provider-specific product ID (e.g. Creem product_ids_mapping)
  const resolvedProvider = provider || pm.getDefaultProvider()?.name;
  const isOptimizer = paymentOrder.productId === OPTIMIZER_PRODUCT_ID;
  if (
    isOptimizer &&
    (!['stripe', 'waffo'].includes(resolvedProvider || '') ||
      paymentOrder.type !== PaymentType.ONE_TIME ||
      paymentOrder.price?.amount !== OPTIMIZER_PRICE_IN_CENTS ||
      paymentOrder.price?.currency !== OPTIMIZER_CURRENCY ||
      paymentOrder.discount)
  ) {
    throw new Error('Invalid optimizer checkout configuration');
  }
  let resolvedProductId = paymentOrder.productId;
  if (resolvedProvider === 'waffo') {
    if (!isOptimizer)
      throw new Error(
        'Waffo currently supports Optimizer lifetime purchases only'
      );
    resolvedProductId = configs.waffo_product_id;
  }
  if (resolvedProvider === 'creem' && paymentOrder.productId) {
    const mapping = configs.creem_product_ids_mapping;
    if (mapping) {
      try {
        const map = JSON.parse(mapping) as Record<string, string>;
        if (map[paymentOrder.productId]) {
          resolvedProductId = map[paymentOrder.productId];
        }
      } catch {
        // invalid JSON — fall through with original productId
      }
    }
  }

  const finalSuccessUrl =
    paymentOrder.successUrl || `${appUrl}/settings/billing?success=1`;
  const callbackSuccessUrl = `${appUrl}/api/payment/callback?order_no=${orderNo}&redirect=${encodeURIComponent(finalSuccessUrl)}`;

  const session = await pm.createPayment({
    order: {
      ...paymentOrder,
      productId: resolvedProductId,
      orderNo,
      metadata: {
        ...paymentOrder.metadata,
        orderNo,
        userId,
        productId: paymentOrder.productId || '',
        ...(isOptimizer ? { licenseProduct: 'true' } : {}),
      },
      successUrl: callbackSuccessUrl,
      cancelUrl:
        paymentOrder.cancelUrl || `${appUrl}/settings/billing?canceled=1`,
    },
    provider: resolvedProvider,
  });

  await db()
    .insert(order)
    .values({
      id: getUuid(),
      orderNo,
      userId,
      userEmail: userEmail || '',
      status: OrderStatus.CREATED,
      amount: paymentOrder.price?.amount || 0,
      currency: paymentOrder.price?.currency || 'usd',
      productId: paymentOrder.productId || '',
      productName: productName || null,
      planName: planName || null,
      creditsAmount: credits ?? null,
      creditsValidDays: creditsValidDays ?? null,
      paymentType: paymentOrder.type || 'one-time',
      paymentProvider: session.provider,
      paymentProductId:
        resolvedProvider === 'waffo' ? resolvedProductId : undefined,
      paymentSessionId: session.checkoutInfo.sessionId,
      checkoutInfo: JSON.stringify(session.checkoutInfo),
      checkoutResult: JSON.stringify(session.checkoutResult),
      checkoutUrl: session.checkoutInfo.checkoutUrl,
      description: paymentOrder.description || '',
    });

  return session;
}

// --- Payment callback (return_url) ---

export async function handlePaymentCallback(orderNo: string) {
  // Find the order
  const [existingOrder] = await db()
    .select()
    .from(order)
    .where(eq(order.orderNo, orderNo))
    .limit(1);

  if (!existingOrder) return;
  if (
    existingOrder.status === OrderStatus.PAID ||
    existingOrder.status === OrderStatus.REFUNDED
  )
    return existingOrder.id;

  // Query the payment provider for latest status
  const pm = await getPaymentManager();
  const provider = pm.getProvider(existingOrder.paymentProvider);
  if (!provider) return;

  const session = await provider.getPaymentSession({
    sessionId: existingOrder.paymentSessionId || existingOrder.orderNo,
  });

  // Reuse the same atomic success handler as the webhook so that
  // subscriptions are created and credits granted on synchronous return too.
  // This is important in environments where webhooks aren't reachable (e.g. localhost).
  await handleCheckoutSuccess(session, existingOrder.paymentProvider);
  return existingOrder.id;
}

// --- Webhook handling ---

export async function handleWebhook(params: {
  req: Request;
  provider: string;
}): Promise<PaymentEvent> {
  const pm = await getPaymentManager();
  const event = await pm.getPaymentEvent({
    req: params.req,
    provider: params.provider,
  });
  const session = event.paymentSession;
  if (!session) return event;

  const eventType = event.eventType;

  // Route event to appropriate handler
  if (eventType === 'checkout.success') {
    const id = await handleCheckoutSuccess(session, params.provider);
    event.orderIds = id ? [id] : [];
  } else if (eventType === 'payment.success') {
    if (
      session.paymentInfo?.subscriptionCycleType ===
      SubscriptionCycleType.RENEWAL
    ) {
      await handleSubscriptionRenewal(session, params.provider);
    } else {
      const id = await handleCheckoutSuccess(session, params.provider);
      event.orderIds = id ? [id] : [];
    }
  } else if (eventType === 'payment.refunded') {
    event.orderIds = await handlePaymentRefund(session, params.provider);
  } else if (eventType === 'subscribe.updated') {
    await handleSubscriptionUpdated(session, params.provider);
  } else if (eventType === 'subscribe.canceled') {
    await handleSubscriptionCanceled(session, params.provider);
  }

  return event;
}

// --- Checkout Success: update order + create subscription + grant credits ---

export async function handleCheckoutSuccess(
  session: PaymentSession,
  provider: string
) {
  // Different providers expose the session identifier under different keys.
  // We try the common shapes; for Alipay the natural key is out_trade_no
  // (which equals our orderNo and the value we stored in paymentSessionId).
  const result = session.paymentResult || {};
  const sessionId: string =
    result.id ||
    result.object?.id ||
    result.out_trade_no ||
    result.outTradeNo ||
    '';
  if (!sessionId) return;

  // Find order by session ID
  const [existingOrder] = await db()
    .select()
    .from(order)
    .where(
      and(
        eq(order.paymentSessionId, sessionId),
        eq(order.paymentProvider, provider),
        isNull(order.deletedAt)
      )
    )
    .limit(1);

  if (!existingOrder) {
    if (session.metadata?.licenseProduct === 'true')
      throw new Error('Optimizer order is not available yet');
    return;
  }

  // Idempotency: skip if already paid
  if (existingOrder.status === OrderStatus.PAID) return existingOrder.id;
  if (
    existingOrder.status !== OrderStatus.CREATED &&
    existingOrder.status !== OrderStatus.PENDING
  )
    return;

  const paymentInfo = session.paymentInfo;
  const subscriptionInfo = session.subscriptionInfo;

  if (session.paymentStatus === PaymentStatus.SUCCESS) {
    if (existingOrder.productId === OPTIMIZER_PRODUCT_ID) {
      validateOptimizerPayment(existingOrder, session, provider);
    }
    // Prepare order update
    const orderUpdate: Record<string, any> = {
      status: OrderStatus.PAID,
      paymentResult: JSON.stringify(session.paymentResult),
      paymentAmount: paymentInfo?.paymentAmount || null,
      paymentCurrency: paymentInfo?.paymentCurrency || null,
      paymentEmail: paymentInfo?.paymentEmail || null,
      paidAt: paymentInfo?.paidAt || new Date(),
      transactionId: paymentInfo?.transactionId || null,
      invoiceId: paymentInfo?.invoiceId || null,
      invoiceUrl: paymentInfo?.invoiceUrl || null,
      paymentUserName: paymentInfo?.paymentUserName || null,
      paymentUserId: paymentInfo?.paymentUserId || null,
      discountCode: paymentInfo?.discountCode || null,
      discountAmount: paymentInfo?.discountAmount || null,
    };

    // This product has no credits/subscription side effects. A single conditional
    // update is atomic on D1 and prevents an older success event undoing a refund.
    if (existingOrder.productId === OPTIMIZER_PRODUCT_ID) {
      await db()
        .update(order)
        .set(orderUpdate)
        .where(
          and(
            eq(order.id, existingOrder.id),
            inArray(order.status, [OrderStatus.CREATED, OrderStatus.PENDING])
          )
        );
      return existingOrder.id;
    }

    // Atomically update order + create subscription + grant credits
    await db().transaction(async (tx: any) => {
      // 1. Create subscription if applicable
      if (subscriptionInfo && session.subscriptionId) {
        const subNo = getSnowId();
        const newSub: any = {
          id: getUuid(),
          subscriptionNo: subNo,
          userId: existingOrder.userId,
          userEmail:
            existingOrder.userEmail || existingOrder.paymentEmail || '',
          status: subscriptionInfo.status || SubscriptionStatus.ACTIVE,
          paymentProvider: provider,
          subscriptionId: session.subscriptionId,
          subscriptionResult: JSON.stringify(session.subscriptionResult),
          productId: existingOrder.productId,
          description: subscriptionInfo.description || 'Subscription Created',
          amount: subscriptionInfo.amount,
          currency: subscriptionInfo.currency,
          interval: subscriptionInfo.interval,
          intervalCount: subscriptionInfo.intervalCount,
          trialPeriodDays: subscriptionInfo.trialPeriodDays,
          currentPeriodStart: subscriptionInfo.currentPeriodStart,
          currentPeriodEnd: subscriptionInfo.currentPeriodEnd,
          billingUrl: subscriptionInfo.billingUrl,
          planName: existingOrder.planName || existingOrder.productName,
          productName: existingOrder.productName,
          creditsAmount: existingOrder.creditsAmount,
          creditsValidDays: existingOrder.creditsValidDays,
          paymentProductId: existingOrder.paymentProductId,
          paymentUserId: paymentInfo?.paymentUserId,
        };
        await tx.insert(subscription).values(newSub);
        orderUpdate.subscriptionNo = subNo;
        orderUpdate.subscriptionId = session.subscriptionId;
        orderUpdate.subscriptionResult = JSON.stringify(
          session.subscriptionResult
        );
      }

      // 2. Grant credits if applicable
      if (existingOrder.creditsAmount && existingOrder.creditsAmount > 0) {
        const credits = existingOrder.creditsAmount;
        const expiresAt = calculateCreditExpirationTime({
          creditsValidDays: existingOrder.creditsValidDays || 0,
          currentPeriodEnd: subscriptionInfo?.currentPeriodEnd,
        });

        await tx.insert(credit).values({
          id: getUuid(),
          userId: existingOrder.userId,
          userEmail: existingOrder.userEmail || '',
          orderNo: existingOrder.orderNo,
          subscriptionNo: orderUpdate.subscriptionNo || '',
          transactionNo: getSnowId(),
          transactionType: 'grant',
          transactionScene:
            existingOrder.paymentType === 'subscription'
              ? 'subscription'
              : 'payment',
          credits,
          remainingCredits: credits,
          description: 'Grant credit',
          expiresAt,
          status: 'active',
        });
      }

      // 3. Update order
      await tx
        .update(order)
        .set(orderUpdate)
        .where(eq(order.id, existingOrder.id));
    });
  } else if (
    session.paymentStatus === PaymentStatus.FAILED ||
    session.paymentStatus === PaymentStatus.CANCELED
  ) {
    await db()
      .update(order)
      .set({
        status: OrderStatus.FAILED,
        paymentResult: JSON.stringify(session.paymentResult),
      })
      .where(
        and(
          eq(order.id, existingOrder.id),
          inArray(order.status, [OrderStatus.CREATED, OrderStatus.PENDING])
        )
      );
  }
  return existingOrder.id;
}

export function validateOptimizerPayment(
  existingOrder: Order,
  session: PaymentSession,
  provider: string
) {
  if (
    session.provider !== provider ||
    !hasOptimizerPaymentProof(
      existingOrder,
      session.paymentResult,
      session.paymentInfo,
      provider
    )
  ) {
    throw new Error('Optimizer payment does not match the authoritative order');
  }
}

/** Any verified positive refund revokes the single-device entitlement. */
export async function handlePaymentRefund(
  session: PaymentSession,
  provider: string
): Promise<string[]> {
  if (provider === 'waffo' && session.provider === 'waffo') {
    const result = session.paymentResult;
    if (typeof result?.id !== 'string')
      throw new Error('Missing Waffo refund order reference');
    const [candidate]: Order[] = await db()
      .select()
      .from(order)
      .where(
        and(
          eq(order.paymentProvider, 'waffo'),
          eq(order.orderNo, result.id),
          eq(order.productId, OPTIMIZER_PRODUCT_ID),
          isNull(order.deletedAt)
        )
      )
      .limit(1);
    if (!candidate) {
      if (session.metadata?.licenseProduct === 'true')
        throw new Error('Refund order is not available yet');
      return [];
    }
    if (!hasWaffoRefundProof(candidate, result))
      throw new Error('Refund does not match optimizer order');
    await db()
      .update(order)
      .set({ status: OrderStatus.REFUNDED })
      .where(eq(order.id, candidate.id));
    return [candidate.id];
  }
  if (provider !== 'stripe' || session.provider !== 'stripe') return [];
  const result = session.paymentResult;
  if (
    !result ||
    result.object !== 'charge' ||
    !Number.isSafeInteger(result.amount_refunded) ||
    result.amount_refunded <= 0
  )
    return [];
  const orderNo = session.metadata?.orderNo;
  const transactionId = session.paymentInfo?.transactionId;
  if (!orderNo && !transactionId) return [];
  const candidates: Order[] = await db()
    .select()
    .from(order)
    .where(
      and(
        eq(order.paymentProvider, provider),
        eq(order.productId, OPTIMIZER_PRODUCT_ID),
        or(
          orderNo ? eq(order.orderNo, orderNo) : undefined,
          transactionId ? eq(order.transactionId, transactionId) : undefined
        )
      )
    );
  if (!candidates.length && session.metadata?.licenseProduct === 'true')
    throw new Error('Refund order is not available yet');
  const ids: string[] = [];
  for (const candidate of candidates) {
    const checkout = JSON.parse(candidate.checkoutResult || '{}');
    if (
      result.livemode !== checkout.livemode ||
      result.currency !== candidate.currency ||
      result.amount !== candidate.amount
    )
      throw new Error('Refund does not match optimizer order');
    await db()
      .update(order)
      .set({ status: OrderStatus.REFUNDED })
      .where(eq(order.id, candidate.id));
    ids.push(candidate.id);
  }
  return ids;
}

// --- Subscription Renewal ---

export async function handleSubscriptionRenewal(
  session: any,
  provider: string
) {
  if (!session.subscriptionId || !session.subscriptionInfo) return;

  const existingSub = await findByProviderSubscriptionId({
    provider,
    subscriptionId: session.subscriptionId,
  });
  if (!existingSub || !existingSub.amount || !existingSub.currency) return;

  const subscriptionInfo = session.subscriptionInfo;
  if (
    !subscriptionInfo.currentPeriodStart ||
    !subscriptionInfo.currentPeriodEnd
  )
    return;

  if (session.paymentStatus !== PaymentStatus.SUCCESS) return;

  const paymentInfo = session.paymentInfo;

  // Idempotency: drop duplicate renewals for the same provider transaction.
  if (paymentInfo?.transactionId) {
    const [dup] = await db()
      .select({ id: order.id })
      .from(order)
      .where(
        and(
          eq(order.transactionId, paymentInfo.transactionId),
          eq(order.paymentProvider, provider)
        )
      )
      .limit(1);
    if (dup) return;
  }

  const renewalOrderNo = getSnowId();

  await db().transaction(async (tx: any) => {
    // 1. Update subscription period
    await tx
      .update(subscription)
      .set({
        currentPeriodStart: subscriptionInfo.currentPeriodStart,
        currentPeriodEnd: subscriptionInfo.currentPeriodEnd,
      })
      .where(eq(subscription.subscriptionNo, existingSub.subscriptionNo));

    // 2. Create renewal order
    await tx.insert(order).values({
      id: getUuid(),
      orderNo: renewalOrderNo,
      userId: existingSub.userId,
      userEmail: existingSub.userEmail || '',
      status: OrderStatus.PAID,
      amount: existingSub.amount,
      currency: existingSub.currency,
      productId: existingSub.productId || '',
      paymentType: 'renew',
      paymentInterval: existingSub.interval || '',
      paymentProvider: provider,
      checkoutInfo: '',
      description: 'Subscription Renewal',
      productName: existingSub.productName || '',
      planName: existingSub.planName || '',
      creditsAmount: existingSub.creditsAmount,
      creditsValidDays: existingSub.creditsValidDays,
      paymentProductId: existingSub.paymentProductId || '',
      paymentResult: JSON.stringify(session.paymentResult),
      paymentAmount: paymentInfo?.paymentAmount,
      paymentCurrency: paymentInfo?.paymentCurrency,
      paymentEmail: paymentInfo?.paymentEmail,
      paidAt: paymentInfo?.paidAt || new Date(),
      invoiceId: paymentInfo?.invoiceId,
      invoiceUrl: paymentInfo?.invoiceUrl,
      subscriptionNo: existingSub.subscriptionNo,
      subscriptionId: session.subscriptionId,
      transactionId: paymentInfo?.transactionId,
      paymentUserName: paymentInfo?.paymentUserName,
      paymentUserId: paymentInfo?.paymentUserId,
    });

    // 3. Grant credits for renewal
    if (existingSub.creditsAmount && existingSub.creditsAmount > 0) {
      const credits = existingSub.creditsAmount;
      const expiresAt = calculateCreditExpirationTime({
        creditsValidDays: existingSub.creditsValidDays || 0,
        currentPeriodEnd: subscriptionInfo.currentPeriodEnd,
      });

      await tx.insert(credit).values({
        id: getUuid(),
        userId: existingSub.userId,
        userEmail: existingSub.userEmail || '',
        orderNo: renewalOrderNo,
        subscriptionNo: existingSub.subscriptionNo,
        transactionNo: getSnowId(),
        transactionType: 'grant',
        transactionScene: 'renewal',
        credits,
        remainingCredits: credits,
        description: 'Grant credit',
        expiresAt,
        status: 'active',
      });
    }
  });
}

// --- Subscription Updated ---

async function handleSubscriptionUpdated(session: any, provider: string) {
  if (!session.subscriptionId || !session.subscriptionInfo) return;

  const existingSub = await findByProviderSubscriptionId({
    provider,
    subscriptionId: session.subscriptionId,
  });
  if (!existingSub) return;

  const info = session.subscriptionInfo;
  await updateBySubscriptionNo(existingSub.subscriptionNo, {
    status: info.status,
    currentPeriodStart: info.currentPeriodStart,
    currentPeriodEnd: info.currentPeriodEnd,
    canceledAt: info.canceledAt || null,
    canceledEndAt: info.canceledEndAt || null,
    canceledReason: info.canceledReason || '',
    canceledReasonType: info.canceledReasonType || '',
  });
}

// --- Subscription Canceled ---

async function handleSubscriptionCanceled(session: any, provider: string) {
  if (!session.subscriptionId || !session.subscriptionInfo) return;

  const existingSub = await findByProviderSubscriptionId({
    provider,
    subscriptionId: session.subscriptionId,
  });
  if (!existingSub) return;

  const info = session.subscriptionInfo;
  await updateBySubscriptionNo(existingSub.subscriptionNo, {
    status: SubscriptionStatus.CANCELED,
    canceledAt: info.canceledAt,
    canceledEndAt: info.canceledEndAt,
    canceledReason: info.canceledReason,
    canceledReasonType: info.canceledReasonType,
  });
}

// --- Cancel subscription (user-initiated) ---

export async function cancelUserSubscription(params: {
  userId: string;
  subscriptionNo: string;
}) {
  const { userId, subscriptionNo } = params;

  const sub = await findBySubscriptionNo(subscriptionNo);
  if (!sub) throw new Error('Subscription not found');
  if (sub.userId !== userId) throw new Error('Forbidden');

  if (
    sub.status === SubscriptionStatus.CANCELED ||
    sub.status === SubscriptionStatus.EXPIRED
  ) {
    return sub;
  }

  const pm = await getPaymentManager();
  const provider = pm.getProvider(sub.paymentProvider);
  if (!provider || !provider.cancelSubscription) {
    throw new Error('Cancellation not supported for this provider');
  }

  const session = await provider.cancelSubscription({
    subscriptionId: sub.subscriptionId,
  });

  const info = session.subscriptionInfo;
  const updated = await updateBySubscriptionNo(subscriptionNo, {
    status: info?.status || SubscriptionStatus.CANCELED,
    canceledAt: info?.canceledAt || new Date(),
    canceledEndAt: info?.canceledEndAt || null,
    canceledReason: info?.canceledReason || 'Canceled by user',
    canceledReasonType: info?.canceledReasonType || 'user_request',
  });

  return updated;
}

// --- Query helpers ---

export async function getUserOrders(userId: string) {
  return db()
    .select()
    .from(order)
    .where(and(eq(order.userId, userId), isNull(order.deletedAt)))
    .orderBy(desc(order.createdAt));
}

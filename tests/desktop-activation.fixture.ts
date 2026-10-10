import { randomBytes, randomUUID } from 'node:crypto';
import { resolve, sep } from 'node:path';
import { and, eq, inArray, or } from 'drizzle-orm';

/** This helper is deliberately outside application routes and only targets local development. */
export async function createDesktopActivationFixture(serverUrl: string) {
  const server = new URL(serverUrl);
  if (
    !['localhost', '127.0.0.1', '[::1]'].includes(server.hostname) ||
    !['http:', 'https:'].includes(server.protocol) ||
    server.pathname !== '/' ||
    server.search ||
    server.hash ||
    server.username ||
    server.password
  ) {
    throw new Error(
      'Activation integration fixtures are restricted to a loopback server origin'
    );
  }
  if (
    process.env.NODE_ENV === 'production' ||
    process.env.DATABASE_PROVIDER !== 'sqlite'
  ) {
    throw new Error(
      'Activation integration fixtures require a development SQLite database'
    );
  }
  const configuredUrl = process.env.DATABASE_URL || '';
  if (!configuredUrl.startsWith('file:'))
    throw new Error('A local test database is required');
  const filePath = resolve(configuredUrl.slice(5));
  const localData = resolve('data') + sep;
  if (
    !filePath.startsWith(localData) ||
    !/^optimizer-(?:dev|activation-test)(?:-[\w-]+)?\.db$/.test(
      filePath.slice(localData.length)
    )
  ) {
    throw new Error(
      'Fixture writes are restricted to data/optimizer-dev.db or an optimizer-activation-test database'
    );
  }
  const { db } = await import('../src/core/db');
  const schema = await import('../src/config/db/schema');
  const { licenses } = await import('../src/modules/licenses/service');
  const { OPTIMIZER_PRODUCT_ID } = await import('../src/config/optimizer');
  const database = db();
  const runId = randomUUID();
  const accounts: { id: string; email: string; password: string }[] = [];
  const orderId = randomUUID();
  let licenseId = '';

  async function cleanup(installationIds: string[] = []) {
    const userIds = accounts.map((account) => account.id);
    if (licenseId || installationIds.length) {
      await database
        .delete(schema.desktopAuthGrant)
        .where(
          or(
            licenseId
              ? eq(schema.desktopAuthGrant.licenseId, licenseId)
              : undefined,
            installationIds.length
              ? inArray(schema.desktopAuthGrant.installationId, installationIds)
              : undefined
          )
        );
    }
    if (licenseId) {
      await database
        .delete(schema.licenseEvent)
        .where(eq(schema.licenseEvent.licenseId, licenseId));
      await database
        .delete(schema.deviceActivation)
        .where(eq(schema.deviceActivation.licenseId, licenseId));
      await database
        .delete(schema.productLicense)
        .where(eq(schema.productLicense.id, licenseId));
    }
    await database.delete(schema.order).where(eq(schema.order.id, orderId));
    if (userIds.length) {
      await database
        .delete(schema.licenseEvent)
        .where(inArray(schema.licenseEvent.actorUserId, userIds));
      await database
        .delete(schema.userRole)
        .where(inArray(schema.userRole.userId, userIds));
      await database
        .delete(schema.credit)
        .where(inArray(schema.credit.userId, userIds));
      await database
        .delete(schema.session)
        .where(inArray(schema.session.userId, userIds));
      await database
        .delete(schema.account)
        .where(inArray(schema.account.userId, userIds));
      await database.delete(schema.verification).where(
        inArray(
          schema.verification.identifier,
          accounts.map((account) => account.email)
        )
      );
      // Check both IDs and the unique fixture email before deleting users.
      for (const account of accounts) {
        await database
          .delete(schema.user)
          .where(
            and(
              eq(schema.user.id, account.id),
              eq(schema.user.email, account.email)
            )
          );
      }
    }
    database.$client.close();
  }

  try {
    for (const role of ['buyer', 'other']) {
      const email = `optimizer-e2e-${runId}-${role}@example.invalid`;
      const password = randomBytes(24).toString('base64url');
      const response = await fetch(new URL('/api/auth/sign-up/email', server), {
        method: 'POST',
        redirect: 'error',
        headers: { 'Content-Type': 'application/json', Origin: server.origin },
        body: JSON.stringify({
          email,
          password,
          name: 'Local integration fixture',
        }),
      });
      if (!response.ok)
        throw new Error(
          `Test account registration returned HTTP ${response.status}`
        );
      const body = (await response.json()) as { user?: { id?: string } };
      if (!body.user?.id)
        throw new Error('Test account registration returned no user identity');
      const account = { id: body.user.id, email, password };
      accounts.push(account);
      const [persisted] = await database
        .select({ id: schema.user.id })
        .from(schema.user)
        .where(
          and(eq(schema.user.id, account.id), eq(schema.user.email, email))
        );
      if (!persisted)
        throw new Error(
          'The test runner and server are not using the same development database'
        );
    }
    const orderNo = `TEST_${runId}`;
    const paymentSessionId = `cs_test_fixture_${runId}`;
    const transactionId = `pi_test_fixture_${runId}`;
    // A clearly marked local fixture, never a checkout route or a production grant path.
    const result = {
      id: paymentSessionId,
      object: 'checkout.session',
      mode: 'payment',
      status: 'complete',
      payment_status: 'paid',
      amount_total: 5777,
      amount_subtotal: 5777,
      currency: 'usd',
      livemode: false,
      total_details: { amount_discount: 0, amount_tax: 0 },
      payment_intent: transactionId,
      metadata: {
        orderNo,
        productId: OPTIMIZER_PRODUCT_ID,
        licenseProduct: 'true',
        fixture: runId,
      },
    };
    await database.insert(schema.order).values({
      id: orderId,
      orderNo,
      userId: accounts[0].id,
      userEmail: accounts[0].email,
      status: 'paid',
      amount: 5777,
      currency: 'usd',
      productId: OPTIMIZER_PRODUCT_ID,
      paymentType: 'one-time',
      paymentProvider: 'stripe',
      paymentSessionId,
      transactionId,
      checkoutInfo: '{}',
      checkoutResult: JSON.stringify({ id: paymentSessionId, livemode: false }),
      paymentResult: JSON.stringify(result),
      paymentAmount: 5777,
      paymentCurrency: 'usd',
      paidAt: new Date(),
      description: 'LOCAL INTEGRATION FIXTURE — no real payment',
    });
    const license = await licenses.fulfillOrder(orderId);
    if (!license)
      throw new Error(
        'The local fixture did not satisfy the normal license fulfillment checks'
      );
    licenseId = license.id;
    return { buyer: accounts[0], other: accounts[1], licenseId, cleanup };
  } catch (error) {
    await cleanup();
    throw error;
  }
}

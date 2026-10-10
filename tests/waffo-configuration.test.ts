import assert from 'node:assert/strict';
import test from 'node:test';

import {
  optimizerCheckoutEnabled,
  optimizerPaymentProvider,
} from '../src/config/optimizer';
import { PaymentManager } from '../src/core/payment';
import { PaymentProvider } from '../src/core/payment/types';
import {
  filterPublicConfigs,
  isSecretConfigKey,
  maskConfigValue,
} from '../src/modules/config/service';
import { getSettings } from '../src/modules/config/settings';

const configured = {
  app_url: 'https://shop.example.invalid',
  default_payment_provider: 'waffo',
  waffo_enabled: 'true',
  waffo_environment: 'test',
  waffo_merchant_id: 'MER_test',
  waffo_store_id: 'STO_test',
  waffo_product_id: 'PROD_test',
  waffo_private_key: 'private-key-placeholder',
  waffo_webhook_public_key: 'public-key-placeholder',
};

test('Optimizer selects only a configured provider and requires license signing', () => {
  assert.equal(optimizerPaymentProvider(configured), 'waffo');
  for (const key of Object.keys(configured).filter((key) =>
    key.startsWith('waffo_')
  )) {
    assert.equal(
      optimizerPaymentProvider({ ...configured, [key]: '' }),
      null,
      key
    );
  }
  assert.equal(
    optimizerPaymentProvider({
      ...configured,
      app_url: 'http://localhost:3000',
    }),
    null
  );
  assert.equal(
    optimizerPaymentProvider({
      ...configured,
      waffo_environment: 'production',
    }),
    null
  );
  assert.equal(optimizerPaymentProvider(configured, 'paypal'), null);
  assert.equal(optimizerPaymentProvider(configured, 'missing'), null);

  const stripe = {
    stripe_enabled: 'true',
    stripe_secret_key: 'sk_test_placeholder',
    stripe_signing_secret: 'whsec_placeholder',
  };
  assert.equal(optimizerPaymentProvider(stripe), 'stripe');
  assert.equal(
    optimizerPaymentProvider({
      ...configured,
      ...stripe,
      default_payment_provider: '',
    }),
    'stripe'
  );
  assert.equal(
    optimizerPaymentProvider({ ...stripe, default_payment_provider: 'creem' }),
    'stripe'
  );
  assert.equal(
    optimizerPaymentProvider({ ...configured, ...stripe }, 'stripe'),
    'stripe'
  );
  assert.equal(
    optimizerPaymentProvider({
      ...configured,
      ...stripe,
      waffo_enabled: 'false',
    }),
    null
  );

  const beforeKey = process.env.OPTIMIZER_LICENSE_PRIVATE_KEY;
  const beforeId = process.env.OPTIMIZER_LICENSE_KEY_ID;
  try {
    delete process.env.OPTIMIZER_LICENSE_PRIVATE_KEY;
    delete process.env.OPTIMIZER_LICENSE_KEY_ID;
    assert.equal(optimizerCheckoutEnabled(configured), false);
    process.env.OPTIMIZER_LICENSE_PRIVATE_KEY = 'configured-signing-key';
    process.env.OPTIMIZER_LICENSE_KEY_ID = 'test';
    assert.equal(optimizerCheckoutEnabled(configured), true);
  } finally {
    if (beforeKey === undefined)
      delete process.env.OPTIMIZER_LICENSE_PRIVATE_KEY;
    else process.env.OPTIMIZER_LICENSE_PRIVATE_KEY = beforeKey;
    if (beforeId === undefined) delete process.env.OPTIMIZER_LICENSE_KEY_ID;
    else process.env.OPTIMIZER_LICENSE_KEY_ID = beforeId;
  }
});

test('Waffo private key is masked and excluded from public configuration', () => {
  assert.equal(isSecretConfigKey('waffo_private_key'), true);
  assert.notEqual(
    maskConfigValue(configured.waffo_private_key),
    configured.waffo_private_key
  );
  assert.equal(
    getSettings().find((setting) => setting.name === 'waffo_environment')
      ?.defaultValue,
    'test'
  );
  assert.deepEqual(
    filterPublicConfigs(configured, ['waffo_enabled', 'waffo_private_key']),
    { waffo_enabled: 'true' }
  );
});

test('an explicit unknown provider never falls back to another gateway', async () => {
  const manager = new PaymentManager();
  const provider: PaymentProvider = {
    name: 'stripe',
    configs: {},
    async createPayment() {
      throw new Error('Must not call the default provider');
    },
    async getPaymentSession() {
      throw new Error('Must not call the default provider');
    },
    async getPaymentEvent() {
      throw new Error('Must not call the default provider');
    },
  };
  manager.addProvider(provider, true);
  assert.equal(manager.getDefaultProvider(), provider);
  assert.equal(manager.getProvider('waffo'), undefined);
  await assert.rejects(
    manager.createPayment({ provider: 'waffo', order: {} }),
    /not found/
  );
  await assert.rejects(
    manager.getPaymentEvent({
      provider: 'waffo',
      req: new Request('https://example.invalid'),
    }),
    /not found/
  );
});

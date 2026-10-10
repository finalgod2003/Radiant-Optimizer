# Waffo Pancake Implementation Plan

> Execute the payment adapter and tests as an isolated file-ownership subtask; implement service integration and configuration in this chat. Preserve the existing uncommitted desktop and translation changes.

**Goal:** Let users buy the Optimizer lifetime license through Waffo Pancake at the user's final USD 19.90 price.

**Architecture:** Add a Waffo PaymentProvider, retain the shared checkout route and webhook route, and extend the server payment proof checks. Read credentials from the existing admin configuration system.

**Tech Stack:** TanStack Start, TypeScript, node:crypto, Drizzle, node:test.

## Global constraints

- No database schema changes, added business-logic dependency, or private keys in client-visible configuration.
- Charge USD 19.90 for new purchases, retain validation for existing USD 57.77 quotes, and preserve Stripe purchase support.
- Require verified payment evidence before granting licenses; any positive successful refund revokes access.
- The merchant account exists; the project-specific test store, product, API key, and webhook are now configured.

## Tasks

- [x] Add `src/core/payment/waffo.ts` and `tests/waffo-payment.test.ts`: signed checkout requests, raw-body signature verification, strict actual amounts, environment/store binding, unsupported-event acknowledgment, explicit subscription rejection. Verify with `pnpm exec tsx --test tests/waffo-payment.test.ts`.
- [x] Extend `src/config/optimizer.ts`, `src/modules/payment/service.ts`, `src/modules/licenses/service.ts`, and `src/lib/optimizer-payment-proof.ts`: choose a configured provider, register Waffo, retain order metadata, validate payment proof, and revoke refunded orders. Add Waffo lifecycle cases to `tests/licenses.integration.test.ts`, including duplicates, refund-before-success, manager configuration, key rotation, and Stripe isolation.
- [x] Add the Waffo admin group and bilingual strings, a test-mode checkout check that never charges automatically, and public provider availability. Keep secret masking covered by a configuration test.
- [x] Write `docs/waffo-payment-setup.md` with product creation, test credentials, HTTPS webhook, payment verification, and production transition steps. All 50 TypeScript tests and the production build passed. The normal TypeScript command encounters an unrelated missing fixture inside ignored `artifacts/source-backups`; a check limited to project sources, tests, scripts, and configs passes.
- [x] Create the isolated Radiant Optimizer test store and USD 57.77 one-time product. With action-time user approval, create and save a dedicated test API key. Save local admin config, configure the test webhook, and complete a real Waffo sandbox checkout with its supplied test card. Adapt key parsing to the bare Base64 key format actually displayed in the dashboard, while retaining PEM support.
- [x] Apply the user's final USD 19.90 price to the authoritative catalog, Waffo product, admin labels, test fixtures, and current setup documentation. Preserve payment/refund eligibility for existing USD 57.77 orders, reject that old price on new checkouts, and verify a fresh real Waffo checkout displays USD 19.90.

## Remaining merchant setup

The Waffo sandbox checkout succeeded. Its test refund was submitted and remains processing in Waffo; no actual `refund.succeeded` delivery is confirmed yet. The current online callback returned HTTP 500 with `Payment provider 'waffo' not found`, confirming this implementation has not been deployed. The settings-test purchase intentionally creates no local order/license. The new store also requires business verification before accepting real payments. Remaining work: merchant business verification, production product/key/webhook configuration, deployment, and a full purchase through the application's pricing page to verify online license delivery. No real charge or production configuration change was performed.

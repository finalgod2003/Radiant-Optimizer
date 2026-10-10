# Waffo Pancake payment integration

The Optimizer Premium lifetime product costs USD 19.90 for one Windows device, as specified by the user during integration. Add Waffo as an alternative payment provider within the existing checkout, order, and license system. A dedicated Waffo test store and product are now configured. Existing USD 57.77 orders retain their original payment and refund validation.

## Checkout and configuration

The admin Payment settings collect merchant ID, store ID, one-time product ID, RSA private key, webhook public key, and test/prod environment. Existing secret masking and encryption apply to the private key. Provider configuration stays server-side. The admin selects Waffo as the default provider; Optimizer readiness requires both payment configuration and license signing configuration.

An inline REST adapter signs requests using Waffo's documented RSA-SHA256 canonical request. Checkout supplies the server catalog amount as a tax-inclusive price snapshot, USD currency, a stable local order reference, and server-owned product/user metadata. This preserves the current advertised total. The integration supports the current one-time USD product; subscription products are rejected explicitly.

## Fulfillment and refunds

The callback never grants a license from browser parameters. Waffo's raw-body webhook is verified with the configured environment's public key. Check signature age, environment, store, external order reference, metadata, and actual charged amount before marking the order paid. Waffo webhooks omit checkout session IDs, so the provider's internal correlation ID is the local order number; retain the actual Waffo session ID in the checkout snapshot.

Use chargedAmount, refundedAmount, and originalChargedAmount, not list-price or deprecated amount fallbacks. Missing or mismatched payment proof fails closed. The order stores the verified native webhook for later license eligibility checks. Existing conditional state transitions and unique license-per-order behavior make duplicate deliveries safe. Any successful positive partial or full refund revokes the single-device entitlement, including when a refund arrives before the success event. Success must never undo a refund.

## Verification and setup

Tests use real RSA signatures, mocked Waffo HTTP responses, and an isolated SQLite database. Cover checkout signing, webhook tampering and environment isolation, exact price proof, duplicate notifications, refund ordering, config masking, and existing Stripe behavior. Run the complete TypeScript tests and pnpm build. No schema change is needed.

The merchant must create a one-time USD product and configure the public HTTPS webhook endpoint /api/payment/notify/waffo for order.completed and refund.succeeded. Finish the implementation and setup instructions without inventing credentials or claiming a live transaction was tested.

## Official references

- https://docs.waffo.ai/api-reference/authentication
- https://docs.waffo.ai/api-reference/endpoints/orders/create-checkout-session
- https://docs.waffo.ai/api-reference/webhooks

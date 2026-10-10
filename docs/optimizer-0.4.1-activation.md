# Premium activation — local 0.4.1 release

Enabled on October 10, 2026. The primary Windows app now embeds the public verification key corresponding to the local website's persistent RSA signing key. The generated release manifest advertises `activationAvailable: true`.

## User flow

1. Download and extract `optimizer-0.4.1-win-x64-preview.zip`, then run `Optimizer.exe`.
2. Open **Your license → Activate this PC**.
3. Sign into the configured website, review the device code and approve it with an existing Premium license.
4. The app verifies the signed lease and updates its edition. Refresh is available in the same page; device unbinding is managed through the website account.

One license permits one active device. Verified offline leases last up to seven days after refresh. Free tools and restoration do not require activation. This change enables the activation mechanism; it does not grant a Premium entitlement to an ordinary account or process a payment.

## Local configuration and key handling

- The current package connects to `http://localhost:3000`. This is a local development release, not an externally reachable production deployment. External distribution requires the intended HTTPS website, its persistent server signing configuration and a client built for that origin.
- The private key remains in the ignored `.env.development` server configuration. Only the public SPKI value is embedded in the executable. The release contains no private key, device token, test account or license cache.
- `node scripts/setup-desktop-keys.mjs` now respects dotenv quoting and final assignments. Repeated setup preserves the original key even when it follows blank template values. Incomplete or invalid existing keys fail without overwriting them.
- Keep the signing configuration stable between restarts and builds. Run `pnpm desktop:build` for this activation-enabled package; `--free-preview` intentionally creates a package without activation.
- Selected pre-change source and environment files were retained under ignored `artifacts/source-backups/premium-activation-before-20261010/`. Backed-up TypeScript files use `.bak` suffixes to keep them out of project compilation.

## Verification

- Three key-setup regression tests passed: repeat execution, quoted empty template values and preservation of incomplete/invalid configurations. Test output is checked not to contain the generated private value.
- Twenty-one C# licensing checks passed, including tampering, expiry, unknown keys, Windows DPAPI storage and recovery after unreadable cache data.
- Twenty-one server security/integration tests passed, including entitlement ownership, device slots, signed leases, refunds and request guards.
- Forty-four native-client/HTTP checks passed against actual source API route handlers served with an isolated local SQLite database. The C# client pins the same public keys used by the packaged build. Checks cover approval, RSA verification, refresh, restart/offline cache, unauthorized accounts, one-device enforcement, unbinding and migration. Purchase records were clearly marked local fixtures; no merchant payment occurred. Fixture accounts, grants and protected client files were removed afterward.
- The main site's `/api/desktop/keys` returned HTTP 200 and matched the public key derived from the configured private key and supplied to the desktop build. The private key's encoded and raw bytes were absent from every packaged release file.
- Desktop publish, TypeScript checking, website production build and browser verification passed. The English download page shows 0.4.1 and the activation instructions. The native **Your license** view exposes **Activate this PC** with a Free, unactivated installation.

The HTTP harness uses the source handlers on a separate origin because the production website build pins its public origin to port 3000. It does not change that production origin or use the application's normal database.

## Package

- File: `public/downloads/optimizer-0.4.1-win-x64-preview.zip`
- SHA-256: `ab8c885658101e4c079bd33eab06df3135cdb5e6c7bf6d0d650ec7d27b300c9b`
- Contents: `Optimizer.exe`, local public product configuration, README, release notes, PresentMon executable and its license.
- Download endpoint returned HTTP 200. ZIP contents and manifest hash were verified.
- The unsigned preview retains the backup/recovery behavior of 0.4.0. Premium entitlement is still enforced; enabling activation does not bypass it.

Evidence: `artifacts/desktop/native-activation-0.4.1.log`, `premium-activation-evidence.json`, `activation-server-tests.log`, `licensing-0.4.1-tests.log`, `key-bootstrap-0.4.1-tests.log`, `typecheck-0.4.1.log`, `web-build-0.4.1.log` and `build-0.4.1-activation.log` in the same artifact directory. Native activation view: `public/design-preview/radiant-client-account.png`.

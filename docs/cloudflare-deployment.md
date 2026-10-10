# Cloudflare production deployment

The production Worker is `radiant-optimizer`, serving
<https://radiantoptimizer.xyz> and <https://www.radiantoptimizer.xyz>.

Connect Cloudflare Workers Builds to `finalgod2003/Radiant-Optimizer` with
these settings:

| Setting                       | Value                                    |
| ----------------------------- | ---------------------------------------- |
| Production branch             | `main`                                   |
| Automatic builds              | Enabled                                  |
| Preview builds                | Disabled                                 |
| Root directory                | `/`                                      |
| Build command                 | `node scripts/prepare-cloudflare-ci.mjs` |
| Deploy command                | `pnpm run cf:deploy`                     |
| Build variable `NODE_VERSION` | `22.23.2`                                |
| Build variable `PNPM_VERSION` | `11.19.0`                                |

The preparation step creates ignored `wrangler.jsonc` and `.env.production`
files from the committed `wrangler.production.json`. Existing local files
are preserved. The deploy command builds the Worker with Nitro's
`cloudflare_module` preset and then deploys it. A failed build stops deployment.

Only public configuration and resource identifiers belong in the production
profile. Authentication, configuration encryption, license signing, and provider
secrets remain in Cloudflare or the encrypted application settings. Do not put
them in the build environment or the repository.

Pushes to `main` automatically build and publish the website. Check the commit
SHA and deployment result in the Worker's **Deployments** tab. Other branches do
not deploy. Desktop installers are released separately and are not built here.
The committed profile preserves the `OPTIMIZER_RELEASES` R2 binding and the
public `OPTIMIZER_RELEASE_JSON` manifest. Update that manifest after publishing
a new installer so a later website deployment keeps the latest download.

Database migrations are a separate reviewed operation. This pipeline does not
push schemas or apply migrations. Before merging a change that requires a new
schema, generate and review a compatible migration and apply it to production.
Keep the public production profile in sync when changing domains or bindings.

To reproduce a deployment build from a clean checkout:

```sh
pnpm install --frozen-lockfile
node scripts/prepare-cloudflare-ci.mjs
pnpm build
pnpm run cf:build
```

After a deployment, check `/`, `/zh`, `/api/config/public`, `/sitemap.xml`, and
`/robots.txt` on the production domain. Cloudflare's Version History can restore
the previous Worker version; database migrations require their own recovery plan.

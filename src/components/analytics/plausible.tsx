// Legacy scripts initialize themselves; site-specific pa-*.js scripts use
// the queued init API. Calling init on a legacy script can double-count views.
export function Plausible({ domain, src }: { domain?: string; src: string }) {
  const scriptSrc = src.trim();
  const siteDomain = domain?.trim();
  const isSiteSpecific = /\/pa-[^/?#]+\.js(?:[?#].*)?$/.test(scriptSrc);
  if (!scriptSrc || (!siteDomain && !isSiteSpecific)) return null;
  return (
    <>
      <script
        id="plausible-loader"
        data-domain={siteDomain || undefined}
        src={scriptSrc}
        async
        defer
      />
      {isSiteSpecific ? (
        <script
          id="plausible-init"
          dangerouslySetInnerHTML={{
            __html: `window.plausible=window.plausible||function(){(plausible.q=plausible.q||[]).push(arguments)},plausible.init=plausible.init||function(i){plausible.o=i||{}};plausible.init();`,
          }}
        />
      ) : null}
    </>
  );
}

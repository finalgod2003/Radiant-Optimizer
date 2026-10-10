// Native scripts are included in the SSR HTML. GA4 enhanced measurement
// tracks TanStack Router's History API navigations when enabled in GA4.
export function GoogleAnalytics({ measurementId }: { measurementId: string }) {
  const id = measurementId.trim();
  // Only GA4 Measurement IDs may be interpolated into the inline script.
  if (!/^G-[A-Z0-9]+$/.test(id)) return null;
  return (
    <>
      <script
        id="ga-loader"
        src={`https://www.googletagmanager.com/gtag/js?id=${id}`}
        async
      />
      <script
        id="ga-init"
        async
        dangerouslySetInnerHTML={{
          __html: `window.dataLayer=window.dataLayer||[];function gtag(){dataLayer.push(arguments);}gtag('js',new Date());gtag('config','${id}');`,
        }}
      />
    </>
  );
}

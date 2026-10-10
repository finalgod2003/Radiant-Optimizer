import { useEffect, useState } from 'react';

// Keep client-only form submissions disabled in SSR HTML until React has
// attached the submit handler. Native fallback submissions must still use POST.
export function useHydrated() {
  const [hydrated, setHydrated] = useState(false);
  useEffect(() => setHydrated(true), []);
  return hydrated;
}

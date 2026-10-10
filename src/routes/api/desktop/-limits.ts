/** Small, expiring buckets; a full map fails closed instead of evicting active limits. */
export function createLicenseRateLimiter(maxEntries = 4096, now = Date.now) {
  const buckets = new Map<string, number>();
  let nextSweep = 0;
  return {
    accept(key: string, intervalMs: number): boolean {
      const time = now();
      if (time >= nextSweep || buckets.size >= maxEntries) {
        for (const [identity, deadline] of buckets)
          if (deadline <= time) buckets.delete(identity);
        nextSweep = time + 1000;
      }
      const deadline = buckets.get(key);
      if (deadline !== undefined && deadline > time) return false;
      if (!buckets.has(key) && buckets.size >= maxEntries) return false;
      buckets.set(key, time + intervalMs);
      return true;
    },
    get size() {
      return buckets.size;
    },
  };
}

export function licenseRequesterIdentity(
  request: Request,
  directAddress?: string,
  isCloudflareWorker = false
): string {
  // These headers are authoritative only inside the Cloudflare runtime. Node
  // deployments use the transport address; arbitrary forwarding headers and cookies
  // never allow an anonymous caller to manufacture a new bucket.
  const address = isCloudflareWorker
    ? request.headers.get('cf-connecting-ip') || directAddress
    : directAddress;
  return address && /^[\dA-Fa-f:.]{1,64}$/.test(address)
    ? address
    : 'unknown-peer';
}

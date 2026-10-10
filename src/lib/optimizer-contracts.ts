/** Shared public contracts. Keep server credentials and native operations out of this file. */
export type ProductRelease = {
  version: string;
  downloadUrl: string;
  sha256: string;
  sizeBytes: number;
};

export type OptimizerCatalog = {
  productId: string;
  priceInCents: number;
  currency: string;
  checkoutEnabled: boolean;
  release: ProductRelease | null;
};

export type LicenseSummary = {
  id: string;
  productId: string;
  status: 'active' | 'revoked' | 'refunded';
  createdAt: string;
  device: { id: string; name: string; lastSeenAt: string } | null;
};

export type DesktopStartRequest = {
  deviceName: string;
  installationId: string;
};
export type DesktopStartResponse = {
  deviceCode: string;
  userCode: string;
  verificationUri: string;
  expiresAt: string;
  intervalSeconds: number;
};
export type SignedLicenseLease = {
  payload: string;
  signature: string;
  keyId: string;
};
export type DesktopPollResponse =
  | { status: 'pending' | 'expired' }
  | { status: 'approved'; deviceToken: string; lease: SignedLicenseLease };
export type LicenseLeasePayload = {
  licenseId: string;
  installationId: string;
  deviceId: string;
  productId: string;
  expiresAt: string;
  issuedAt: string;
};

// GET /api/optimizer/catalog -> OptimizerCatalog
// GET /api/licenses -> LicenseSummary[] (signed-in user)
// POST /api/licenses { action: 'unbind', licenseId } -> { ok: true }
// POST /api/desktop/start -> DesktopStartResponse
// POST /api/desktop/approve { userCode, licenseId } -> { ok: true } (signed-in user)
// GET /api/desktop/approve?userCode=... -> { deviceName: string; expiresAt: string } (signed-in user)
// POST /api/desktop/poll { deviceCode } -> DesktopPollResponse
// POST /api/desktop/refresh (Bearer deviceToken) -> { lease: SignedLicenseLease }
// GET /api/desktop/keys -> { keys: { keyId: string; spki: string }[] }
// SPKI, payload and signature are base64url; signature is RSASSA-PKCS1-v1_5 / SHA-256.
// GET /api/admin/licenses?page=1&pageSize=10 -> PageResult<LicenseSummary & { userId: string }>
// POST /api/admin/licenses { licenseId, action: 'revoke' } -> { ok: true }

import { m } from '@/paraglide/messages.js';

export function optimizerErrorMessage(error: Error) {
  if (error.message === 'Unauthorized') return m['optimizer.error.session']();
  if (
    error.message === 'Invalid or expired device code' ||
    error.message === 'Device code was already used'
  )
    return m['optimizer.activate.device_error']();
  if (error.message === 'License already has a device. Unbind it first.')
    return m['optimizer.error.device_bound']();
  if (
    error.message === 'License not found' ||
    error.message === 'License is not active'
  )
    return m['optimizer.error.license_unavailable']();
  if (error.message.startsWith('Too many requests'))
    return m['optimizer.error.rate_limit']();
  return m['optimizer.error.request_failed']();
}
